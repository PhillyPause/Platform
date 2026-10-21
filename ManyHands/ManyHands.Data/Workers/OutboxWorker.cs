using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManyHands.Data.Workers;

/// <summary>
/// Drains the outbox. Claims are atomic and short-lived, so a crash
/// mid-dispatch returns the row to the queue rather than wedging it; handlers
/// are idempotent to make that safe.
/// </summary>
public sealed class OutboxWorker(
    OutboxRepository outbox,
    OutboxDispatcher dispatcher,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                var batch = await outbox.ClaimBatchAsync(ct: stoppingToken);

                foreach (var message in batch)
                {
                    try
                    {
                        await dispatcher.DispatchAsync(message, stoppingToken);
                        await outbox.MarkProcessedAsync(message.Id, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One poisoned message must not stop the queue.
                        logger.LogError(ex, "Outbox {Id} [{Kind}] attempt {Attempts} failed",
                            message.Id, message.Kind, message.Attempts);
                        await outbox.MarkFailedAsync(message.Id, message.Attempts, ex.Message, stoppingToken);
                    }
                }

                // Work found: come straight back for more.
                if (batch.Count > 0) delay = TimeSpan.Zero;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox polling failed; backing off");
                delay = ErrorDelay;
            }

            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Outbox worker stopped");
    }
}
