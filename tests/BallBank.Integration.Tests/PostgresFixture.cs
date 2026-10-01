using BallBank.Api.Features.Treasury;
using BallBank.Integration.Tests.Http;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Npgsql;
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

    // A database of its own: two Solo nodes (Wolverine durability, the projection daemon) must not share one.
    private const string LimitedDatabase = "ratelimits";

    private readonly Lazy<Task<BallBankApi>> _limitedApi;
    private BallBankApi? _api;

    public PostgresFixture() => _limitedApi = new(StartLimitedApi);

    public DocumentStore Store { get; private set; } = default!;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The API hosted in memory over this database; started on first use.</summary>
    public BallBankApi Api => _api ??= new BallBankApi(_container.GetConnectionString());

    /// <summary>
    /// An API of its own whose league, caller and address budgets are five requests that do not come back
    /// (<see cref="LimitedBurst"/>); started on first use.
    /// </summary>
    public Task<BallBankApi> LimitedApi => _limitedApi.Value;

    public const int LimitedBurst = 5;

    private async Task<BallBankApi> StartLimitedApi()
    {
        await _container.ExecScriptAsync($"create database {LimitedDatabase}");
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = LimitedDatabase };
        return new BallBankApi(connection.ConnectionString, RateLimitSettings.Lowered(LimitedBurst));
    }

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

        if (_limitedApi.IsValueCreated)
        {
            await (await _limitedApi.Value).DisposeAsync();
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
