using DbUp;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace ManyHands.Data;

/// <summary>
/// Brings the database up to date with the scripts embedded in this assembly.
/// Run once at startup, before the app takes a request: there is one instance,
/// so nothing else can be migrating at the same time. Roll forward only.
/// </summary>
public static class ManyHandsDatabase
{
    /// <summary>
    /// Seed scripts create fictional volunteers and activities, so they run only
    /// when asked for: a filename containing <c>_seed_</c> is skipped unless
    /// <paramref name="includeSeed"/> is true, and nothing sets that in
    /// production.
    /// </summary>
    public static void Migrate(string connectionString, bool includeSeed, ILogger logger)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(ManyHandsDatabase).Assembly,
                name => includeSeed || !IsSeedScript(name))
            .WithTransactionPerScript()
            // Postgres dollar-quoting ($f$ ... $f$) collides with DbUp's
            // $variable$ substitution, which would fail any script using a named
            // tag. Nothing here uses DbUp variables, so the feature is off.
            .WithVariablesDisabled()
            .LogTo(new DbUpLogger(logger))
            .Build();

        var result = upgrader.PerformUpgrade();

        // A failed migration must stop the app starting: serving pages against
        // a half-built schema fails in ways nobody can read from the outside.
        if (!result.Successful)
            throw new InvalidOperationException(
                $"ManyHands migration failed at {result.ErrorScript?.Name ?? "an unknown script"}.",
                result.Error);
    }

    internal static bool IsSeedScript(string name) =>
        name.Contains("_seed_", StringComparison.OrdinalIgnoreCase);

    /// <summary>DbUp's log, routed into ILogger rather than the console.</summary>
    private sealed class DbUpLogger(ILogger logger) : IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) { }

        public void LogDebug(string format, params object[] args) { }

        public void LogInformation(string format, params object[] args) =>
            logger.LogInformation("{Message}", string.Format(format, args));

        public void LogWarning(string format, params object[] args) =>
            logger.LogWarning("{Message}", string.Format(format, args));

        public void LogError(string format, params object[] args) =>
            logger.LogError("{Message}", string.Format(format, args));

        public void LogError(Exception ex, string format, params object[] args) =>
            logger.LogError(ex, "{Message}", string.Format(format, args));
    }
}
