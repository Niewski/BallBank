using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Treasury;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilient HttpClient (src/BallBank.ServiceDefaults).
builder.AddServiceDefaults();

builder.Services.AddOpenApi();

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
    });
builder.Services.AddAuthorization();

// A refused command (DomainException) is a 409 carrying its message; anything else stays a 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalHandler>();

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

        // Aggregates rebuilt from their streams on read (see MemberAccountProjection for why explicit).
        options.Projections.Add(new MemberAccountProjection(), ProjectionLifecycle.Live);

        // Who caused what: correlation/causation ids and headers (e.g. the acting user) on every event.
        options.Events.MetadataConfig.CausationIdEnabled = true;
        options.Events.MetadataConfig.CorrelationIdEnabled = true;
        options.Events.MetadataConfig.HeadersEnabled = true;
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

// Wolverine: command handlers, HTTP endpoints, transactional outbox, scheduling.
builder.Host.UseWolverine(options =>
{
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableLocalQueues();

    // One node for now (ADR-0007). Revisit when there is more than one replica.
    options.Durability.Mode = DurabilityMode.Solo;
});
builder.Services.AddWolverineHttp();

var app = builder.Build();

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
    options.TenantId.IsRouteArgumentNamed("leagueId");
});

await app.RunAsync();

// Lets integration tests host the API with WebApplicationFactory<Program>.
public partial class Program
{
}
