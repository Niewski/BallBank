using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Api.Features.Treasury;
using BallBank.Api.Integrations.Discord;
using BallBank.Api.Integrations.Sleeper;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Http;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilient HttpClient (src/BallBank.ServiceDefaults).
builder.AddServiceDefaults();

builder.Services.AddOpenApi();

// The one clock: every event stamp, idempotency record and time-based decision reads "now" from it,
// so a test host can swap in a clock it sets.
builder.Services.AddSingleton(TimeProvider.System);

// The web app is served from a different origin (static export on its own host).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Auth0 issues the tokens; the API only validates them (ADR-0008) and reads nothing but the subject.
// Settings come from user-secrets locally (Auth0:Domain, Auth0:Audience) and platform settings when deployed.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var auth0 = builder.Configuration.GetSection("Auth0");
        var domain = auth0["Domain"]
            ?? throw new InvalidOperationException("Auth0:Domain is not configured.");

        options.Authority = $"https://{domain}/";
        options.Audience = auth0["Audience"]
            ?? throw new InvalidOperationException("Auth0:Audience is not configured.");
        options.TokenValidationParameters.ValidIssuer = options.Authority;
        options.MapInboundClaims = false;

        // The subject is who the caller is; a token without one identifies nobody.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("sub") is null)
                {
                    context.Fail("The token carries no subject.");
                }

                return Task.CompletedTask;
            },
        };
    });

// LeagueMember guards a league's pages: the caller's memberships list the league in the route.
builder.Services.AddLeaguePolicies();

// A refused command (DomainException) is a 409 carrying its message; anything else stays a 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalHandler>();

// Sleeper, where leagues are imported from. A Sleeper failure is a 502 that says to try again.
builder.Services.AddSleeper();
builder.Services.AddExceptionHandler<SleeperUnavailableHandler>();

// Discord, where a league is told things (ADR-0008). Its webhook URL is a secret and is kept out of logs and traces.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.Section));
builder.Services.AddDiscord();

// Marten: event store + documents on PostgreSQL. Tenant = league (ADR-0004).
builder.Services.AddMarten(options =>
    {
        options.Connection(builder.Configuration.GetConnectionString("ballbank")
            ?? throw new InvalidOperationException("Connection string 'ballbank' is not configured."));
        options.DatabaseSchemaName = "ballbank";

        // Every event and document row carries a tenant_id; sessions are opened per league.
        options.Events.TenancyStyle = TenancyStyle.Conjoined;
        options.Policies.AllDocumentsAreMultiTenanted();

        // The sanctioned cross-tenant documents (ADR-0011) live in the default tenant.
        options.Schema.For<UserMemberships>().SingleTenanted();
        options.Schema.For<SleeperLeagueIndex>().SingleTenanted();

        // Aggregates rebuilt from their streams on read (see MemberAccountProjection for why explicit).
        options.Projections.Add(new MemberAccountProjection(), ProjectionLifecycle.Live);
        options.Projections.Add(new LeagueProjection(), ProjectionLifecycle.Live);

        options.Projections.Add(new SeasonProjection(), ProjectionLifecycle.Live);

        // Stored so a league's seasons, and a member's statement, can be listed without knowing their
        // stream ids up front; built synchronously, in the same transaction as the events themselves.
        options.Projections.Add(new SeasonListingProjection(), ProjectionLifecycle.Inline);
        options.Projections.Add(new MemberStatementProjection(), ProjectionLifecycle.Inline);

        // The treasurer's dashboard trails the events by a moment, built by the projection daemon (ADR-0006).
        options.Projections.Add(new LeaguePotProjection(), ProjectionLifecycle.Async);

        // Who caused what: correlation/causation ids and headers (e.g. the acting user) on every event.
        options.Events.MetadataConfig.CausationIdEnabled = true;
        options.Events.MetadataConfig.CorrelationIdEnabled = true;
        options.Events.MetadataConfig.HeadersEnabled = true;
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine(integration =>
        // Every committed event is handed to Wolverine by the transaction that commits it, so
        // announcements are decided from what was recorded, never from what an endpoint remembers to say.
        integration.UseFastEventForwarding = true)

    // One node runs the daemon, for now (ADR-0007), like Wolverine's durability below.
    .AddAsyncDaemon(DaemonMode.Solo);

// Wolverine: command handlers, HTTP endpoints, transactional outbox, scheduling.
builder.Host.UseWolverine(options =>
{
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableLocalQueues();

    // One node for now (ADR-0007). Revisit when there is more than one replica.
    options.Durability.Mode = DurabilityMode.Solo;

    // Typed HTTP clients are built by the HTTP client factory, which Wolverine's code generation cannot inline.
    options.CodeGeneration.AlwaysUseServiceLocationFor<SleeperClient>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<DiscordWebhookChannel>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<INotificationChannel>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<NotificationChannels>();

    // Sends are messages of their own, on a durable queue, so a channel that is down delays a notification
    // and nothing else (ADR-0008): retried on a schedule, then dead-lettered (docs/runbook.md).
    options.PublishMessage<SendNotification>().ToLocalQueue(SendNotificationHandler.Queue);
    options.OnException<NotificationDeliveryException>()
        .ScheduleRetry(builder.Configuration.GetSection(NotificationOptions.Section).Get<NotificationOptions>()?.Delays
            ?? NotificationOptions.DefaultRetryDelays)
        .Then.MoveToErrorQueue();
});
builder.Services.AddTransient<NotificationChannels>();
builder.Services.AddWolverineHttp();

var app = builder.Build();

// Routing has already run (WebApplication adds it first), so the league in the route is known here.
app.UseTenantTelemetry();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGet("/v1/version", () => new VersionInfo(
    "BallBank",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));

app.MapWolverineEndpoints(options =>
{
    // The league id in the route is the tenant. Endpoints without it run in the default tenant.
    options.TenantId.DetectWith(new LeagueTenant());

    // A retried mutating request answers as the first try did and records nothing new (ADR-0005).
    options.UseIdempotency();
});

// The JasperFx commands (`projections rebuild`, ...) are served by this host, so the deployed image
// is the one that rebuilds a projection (docs/runbook.md). With no command, this runs the API.
return await app.RunJasperFxCommands(args);

// Lets integration tests host the API with WebApplicationFactory<Program>.
public partial class Program
{
}
