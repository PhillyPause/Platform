using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ManyHands.Data;

public static class ManyHandsServiceCollectionExtensions
{
    /// <summary>True when <c>ManyHands:Enabled</c> is set. Everything else hangs off this.</summary>
    public static bool IsManyHandsEnabled(this IConfiguration configuration) =>
        configuration.GetSection(ManyHandsOptions.SectionName).GetValue<bool>(nameof(ManyHandsOptions.Enabled));

    /// <summary>
    /// The data source, the repositories and the background workers. Call only
    /// when <see cref="IsManyHandsEnabled"/> is true: with it off, nothing here
    /// is registered and nothing reaches for a database.
    /// </summary>
    public static IServiceCollection AddManyHands(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Column mapping must be set before the first query, and a silently
        // unmapped record is far harder to spot than a startup failure.
        DapperConfiguration.Configure();

        var connectionString = configuration.GetConnectionString(ManyHandsOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"ManyHands:Enabled is true but ConnectionStrings:{ManyHandsOptions.ConnectionStringName} is not set.");

        services.AddOptions<ManyHandsOptions>()
            .Bind(configuration.GetSection(ManyHandsOptions.SectionName));

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        return services;
    }

    /// <summary>
    /// Applies pending migrations. Call once, after the app is built and before
    /// it runs; it throws if a script fails, which stops the app starting.
    /// </summary>
    public static void MigrateManyHands(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var options = services.GetRequiredService<IOptions<ManyHandsOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ManyHandsDatabase));

        ManyHandsDatabase.Migrate(
            configuration.GetConnectionString(ManyHandsOptions.ConnectionStringName)!,
            options.Seed,
            logger);
    }
}
