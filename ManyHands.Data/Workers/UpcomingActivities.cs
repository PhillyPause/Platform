using ManyHands.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManyHands.Data.Workers;

/// <summary>
/// What the home page shows of ManyHands, held in memory so a request never
/// waits on Postgres (PLAN.md §2.2). No dependencies, so the host registers it
/// whether ManyHands is on or not: off, nothing fills it, and an empty cache is
/// exactly the signal for the page's static fallback.
/// </summary>
public sealed class UpcomingActivities(TimeProvider clock)
{
    private volatile IReadOnlyList<ActivityListItem> _published = [];

    /// <summary>
    /// The soonest dated activity still to come. Filtered on read, so a cache
    /// that stopped refreshing during an outage stops showing an event once it
    /// has happened, rather than advertising last week.
    /// </summary>
    public ActivityListItem? Next
    {
        get
        {
            var now = clock.GetUtcNow();
            return _published
                .Where(a => a.StartsAt is DateTimeOffset start && (a.EndsAt ?? start) > now)
                .MinBy(a => a.StartsAt);
        }
    }

    internal void Replace(IReadOnlyList<ActivityListItem> published) => _published = published;
}

/// <summary>
/// Keeps <see cref="UpcomingActivities"/> fresh. A failed refresh keeps the
/// last good copy: a short outage should not blank the front page's event box.
/// </summary>
public sealed class UpcomingActivitiesWorker(
    ActivityRepository activities,
    UpcomingActivities cache,
    ILogger<UpcomingActivitiesWorker> logger) : BackgroundService
{
    /// <summary>A published edit shows on the front page within this long.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    internal async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            cache.Replace(await activities.GetPublishedAsync(ct: ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Refreshing the front page's activities failed; keeping the last copy");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
