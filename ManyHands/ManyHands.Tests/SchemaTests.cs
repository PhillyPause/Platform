using Dapper;
using FluentAssertions;
using ManyHands.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManyHands.Tests;

/// <summary>
/// ManyHands serves one organisation and always will (Docs/ManyHands/MANYHANDS.md). The
/// multi-chapter design it came from had a tenant column on every table and a
/// row-level-security policy on each; these pin that none of it comes back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaTests(DatabaseFixture db)
{
    private const int ActivityTypeCount = 12;

    [Fact]
    public async Task No_table_carries_a_tenant_column()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var columns = await conn.QueryAsync<string>(
            """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (column_name LIKE '%chapter%' OR column_name LIKE '%tenant%')
            """);

        columns.Should().BeEmpty();
    }

    [Fact]
    public async Task No_table_has_row_level_security()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var tables = await conn.QueryAsync<string>(
            """
            SELECT relname FROM pg_class
            WHERE relnamespace = 'public'::regnamespace
              AND relkind = 'r'
              AND (relrowsecurity OR relforcerowsecurity)
            """);

        tables.Should().BeEmpty();
    }

    [Fact]
    public async Task There_is_no_chapters_or_memberships_table()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var tables = await conn.QueryAsync<string>(
            """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('chapters', 'memberships')
            """);

        tables.Should().BeEmpty("a volunteer's role lives on the volunteer");
    }

    [Fact]
    public async Task The_interest_vocabulary_is_present()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();

        var count = await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM activity_types");

        count.Should().Be(ActivityTypeCount, "the app is wrong without the reference data");
    }

    [Fact]
    public void Migrating_an_up_to_date_database_changes_nothing()
    {
        var migrate = () => ManyHandsDatabase.Migrate(db.ConnectionString, includeSeed: false, NullLogger.Instance);

        migrate.Should().NotThrow("DbUp journals every script and runs each once");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Seeding_anywhere_but_Development_stops_the_site_starting(string environment)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(Options.Create(new ManyHandsOptions { Enabled = true, Seed = true }))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IHostEnvironment>(new Environment(environment))
            .BuildServiceProvider();

        var migrate = () => services.MigrateManyHands();

        migrate.Should().Throw<InvalidOperationException>().WithMessage("*outside Development*");
    }

    /// <summary>An invented event on the Events list reads as a real one.</summary>
    [Fact]
    public void The_seed_invents_no_activities()
    {
        using var stream = typeof(ManyHandsDatabase).Assembly.GetManifestResourceStream("ManyHands.Data.Scripts.9999_seed_dev.sql");
        var seed = new StreamReader(stream!).ReadToEnd();

        seed.Should().NotContain("INSERT INTO activities").And.NotContain("INSERT INTO signups");
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ManyHands.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Theory]
    [InlineData("ManyHands.Data.Scripts.9999_seed_dev.sql", true)]
    [InlineData("ManyHands.Data.Scripts.0001_schema.sql", false)]
    public void Only_seed_scripts_are_treated_as_seed(string name, bool isSeed) =>
        ManyHandsDatabase.IsSeedScript(name).Should().Be(isSeed);
}
