var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with a named volume so data survives restarts. Marten owns the schema.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("ballbank-postgres");

var database = postgres.AddDatabase("ballbank");

// Auth0 (ADR-0008). Per developer, set once in this project's user-secrets
// (Parameters:auth0-domain, Parameters:auth0-audience, Parameters:auth0-client-id) or the dashboard asks.
var auth0Domain = builder.AddParameter("auth0-domain");
var auth0Audience = builder.AddParameter("auth0-audience");
var auth0ClientId = builder.AddParameter("auth0-client-id");

var api = builder.AddProject<Projects.BallBank_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithExternalHttpEndpoints();

// Next.js dev server. Dependencies are installed automatically; PORT is injected by Aspire.
// The API is reached over http locally so the browser does not need to trust the dev certificate.
builder.AddJavaScriptApp("web", "../../web")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithEnvironment("NEXT_PUBLIC_API_URL", api.GetEndpoint("http"))
    .WithEnvironment("NEXT_PUBLIC_AUTH0_DOMAIN", auth0Domain)
    .WithEnvironment("NEXT_PUBLIC_AUTH0_AUDIENCE", auth0Audience)
    .WithEnvironment("NEXT_PUBLIC_AUTH0_CLIENT_ID", auth0ClientId)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
