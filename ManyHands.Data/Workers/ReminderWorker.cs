using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManyHands.Data.Workers;

/// <summary>
/// Queues activity reminders, and clears out spent sign-in tokens. Reminders
/// go through the outbox rather than sending directly, so retries and
/// idempotency work the same way as every other message.
/// </summary>
public sealed class ReminderWorker(
    NotificationQueries notifications,
    OutboxRepository outbox,
    AuthTokenRepository tokens,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    /// <summary>
    /// A day's notice is enough to rearrange an evening and not so early that
    /// it is forgotten by the time the activity arrives.
    /// </summary>
    internal static readonly TimeSpan Lookahead = TimeSpan.FromHours(24);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                var due = await notifications.GetDueRemindersAsync(Lookahead, stoppingToken);
                foreach (var signupId in due)
                    await outbox.QueueAsync(OutboxKinds.ActivityReminder, new { signupId }, stoppingToken);

                if (due.Count > 0)
                    logger.LogInformation("Queued {Count} activity reminders", due.Count);

                await tokens.PurgeExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder sweep failed; will retry next tick");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
