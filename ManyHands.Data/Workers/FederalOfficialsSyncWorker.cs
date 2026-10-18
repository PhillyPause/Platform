using ManyHands.Data.Lookup;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Workers;

/// <summary>
/// Keeps <c>federal_officials</c> in step with the public
/// <c>unitedstates/congress-legislators</c> dataset, beside the outbox and
/// the reminder sweep.
///
/// Two rules shape everything here. First, it is off by default: development
/// and CI must never make an outbound call nobody asked for, so with
/// <c>FederalOfficials:Enabled</c> unset this service says so once and stops.
/// Second, it cannot take the host down — a directory that is a week stale is
/// an inconvenience, while a host that will not start means no confirmation
/// email goes out at all.
/// </summary>
public sealed class FederalOfficialsSyncWorker(
    IFederalOfficialsSource source,
    FederalOfficialsRepository officials,
    IOptions<FederalOfficialsOptions> options,
    ILogger<FederalOfficialsSyncWorker> logger) : BackgroundService
{
    /// <summary>Recorded on every run in <c>sync_runs</c>.</summary>
    private const string SourceName = "congress-legislators";

    /// <summary>
    /// <c>sync_runs.error</c> is unbounded text, but an exception message
    /// carrying a whole response body helps nobody read the table.
    /// </summary>
    private const int MaxErrorLength = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Federal officials sync is off (FederalOfficials:Enabled is false). " +
                "No request will be made to {Url}.", settings.SourceUrl);
            return;
        }

        using var timer = new PeriodicTimer(settings.RefreshInterval);

        if (!settings.RunOnStartup && !await SafeWaitAsync(timer, stoppingToken))
            return;

        do
        {
            await SyncAsync(settings.RefreshInterval, stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task SyncAsync(TimeSpan refreshInterval, CancellationToken ct)
    {
        long runId;

        try
        {
            if (await officials.GetLastSuccessfulSyncAsync(ct) is { } lastSync)
            {
                var age = DateTimeOffset.UtcNow - lastSync;
                if (age < refreshInterval)
                {
                    // Restarts and redeploys are not a reason to re-download.
                    logger.LogInformation(
                        "Federal directory was synced {AgeHours:N1}h ago; not due yet",
                        age.TotalHours);
                    return;
                }
            }

            runId = await officials.StartSyncAsync(SourceName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No run row was written, so there is nothing to close out.
            logger.LogError(ex, "Could not start the federal officials sync; will retry next tick");
            return;
        }

        try
        {
            var fetched = await source.FetchAsync(ct);

            var upserted = 0;
            var skipped = 0;

            foreach (var official in fetched)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await officials.UpsertAsync(official, ct);
                    upserted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Each upsert has its own connection and its own statement,
                    // so one row the database refuses does not poison the rest.
                    skipped++;
                    logger.LogWarning(ex,
                        "Could not store federal official {BioguideId}", official.BioguideId);
                }
            }

            await officials.FinishSyncAsync(runId, upserted, null, ct);

            logger.LogInformation(
                "Federal directory synced: {Fetched} fetched, {Upserted} upserted, {Skipped} skipped",
                fetched.Count, upserted, skipped);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down. Leave the run open rather than recording a failure
            // that was really just a deploy; the next boot starts a fresh one.
            logger.LogInformation("Federal officials sync cancelled during shutdown");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Federal officials sync failed; the directory keeps its previous contents");
            await TryFinishAsync(runId, Truncate(ex.Message), ct);
        }
    }

    /// <summary>
    /// Closing the run is best-effort by definition: we are already here
    /// because something failed, and the database may be what failed.
    /// </summary>
    private async Task TryFinishAsync(long runId, string error, CancellationToken ct)
    {
        try
        {
            await officials.FinishSyncAsync(runId, records: 0, error, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the failure of sync run {RunId}", runId);
        }
    }

    private static string Truncate(string message) =>
        message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
