using ManyHands.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ManyHands.Tests;

/// <summary>
/// Real Postgres in a container, migrated by the same code the app runs at
/// startup. Docker must be running; CI's runners have it.
///
/// One database for the whole run, shared by every test in the collection, so
/// a test arranges its own rows and asserts on those, never on table totals.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _pg.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        DapperConfiguration.Configure();
        ManyHandsDatabase.Migrate(ConnectionString, includeSeed: false, NullLogger.Instance);

        DataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "postgres";
}
