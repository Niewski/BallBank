using BallBank.Api.Features.Treasury;
using BallBank.Integration.Tests.Http;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
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
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private BallBankApi? _api;

    public DocumentStore Store { get; private set; } = default!;

    /// <summary>The API hosted in memory over this database; started on first use.</summary>
    public BallBankApi Api => _api ??= new BallBankApi(_container.GetConnectionString());

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Store = DocumentStore.For(options =>
        {
            options.Connection(_container.GetConnectionString());
            options.DatabaseSchemaName = "ballbank";
            options.Events.TenancyStyle = TenancyStyle.Conjoined;
            options.Policies.AllDocumentsAreMultiTenanted();
            options.Projections.Add(new MemberAccountProjection(), ProjectionLifecycle.Live);
        });
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        Store.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
