using BallBank.Api;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
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

// Marten: event store + documents on PostgreSQL. Tenant = league (ADR-0004).
builder.Services.AddMarten(options =>
    {
        options.Connection(builder.Configuration.GetConnectionString("ballbank")
            ?? throw new InvalidOperationException("Connection string 'ballbank' is not configured."));
        options.DatabaseSchemaName = "ballbank";

        // Every event and document row carries a tenant_id; sessions are opened per league.
        options.Events.TenancyStyle = TenancyStyle.Conjoined;
        options.Policies.AllDocumentsAreMultiTenanted();

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

app.UseCors();

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
