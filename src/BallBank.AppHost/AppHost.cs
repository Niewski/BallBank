var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with a named volume so data survives restarts. Marten owns the schema.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("ballbank-postgres");

var database = postgres.AddDatabase("ballbank");

var api = builder.AddProject<Projects.BallBank_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithExternalHttpEndpoints();

// Next.js dev server. Dependencies are installed automatically; PORT is injected by Aspire.
// The API is reached over http locally so the browser does not need to trust the dev certificate.
builder.AddJavaScriptApp("web", "../../web")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithEnvironment("NEXT_PUBLIC_API_URL", api.GetEndpoint("http"))
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
