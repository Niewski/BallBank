using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Testcontainers.PostgreSql;

namespace BallBank.Integration.Tests;

/// <summary>
/// One PostgreSQL container and one Marten store per test collection, configured exactly like the API
/// (conjoined tenancy, schema "ballbank") so the tests exercise the real storage rules.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public DocumentStore Store { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Store = DocumentStore.For(options =>
        {
            options.Connection(_container.GetConnectionString());
            options.DatabaseSchemaName = "ballbank";
            options.Events.TenancyStyle = TenancyStyle.Conjoined;
            options.Policies.AllDocumentsAreMultiTenanted();
        });
    }

    public async Task DisposeAsync()
    {
        Store.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
