using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Discord;

/// <summary>
/// Keeps the volunteer role on the people who have linked their Discord
/// account, and takes it back from those who unlinked or left. A periodic sweep
/// rather than a hook, because role state drifts for reasons ManyHands never
/// sees, and only a sweep converges on all of them.
/// </summary>
public sealed class DiscordRoleSyncWorker(
    DiscordQueries discord,
    IDiscordSender sender,
    IOptions<DiscordOptions> options,
    ILogger<DiscordRoleSyncWorker> logger) : BackgroundService
{
    private readonly DiscordOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RoleSyncInterval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Discord role sweep failed; will retry next tick");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        var actions = await discord.GetRoleSyncActionsAsync(_options.RoleId, ct: ct);
        var unfinished = new Dictionary<RoleSyncOutcome, int>();

        foreach (var action in actions)
        {
            var outcome = await sender.SyncRoleAsync(action.RoleId, action.DiscordUserId, action.IsGrant, ct);

            var settled = outcome is RoleSyncOutcome.Applied or RoleSyncOutcome.AlreadyCorrect
                // A role that no longer exists, or a member who has left, is a
                // revoke that already happened another way. Keeping the grant
                // on file would plan it again every sweep, forever.
                || (!action.IsGrant && outcome is RoleSyncOutcome.NotInGuild or RoleSyncOutcome.RoleMissing);

            if (settled && action.IsGrant)
                await discord.RecordRoleGrantAsync(action.VolunteerId, action.DiscordUserId, action.RoleId, ct);
            else if (settled)
                await discord.ClearRoleGrantAsync(action.VolunteerId, ct);
            // Simulated records nothing: the first sweep with a token does the real work.
            else if (outcome is not RoleSyncOutcome.Simulated)
                unfinished[outcome] = unfinished.GetValueOrDefault(outcome) + 1;

            if (_options.SendDelay > TimeSpan.Zero) await Task.Delay(_options.SendDelay, ct);
        }

        // One line per sweep, not one per person: a server the bot cannot
        // manage would otherwise fill the log with the same warning.
        if (unfinished.Count > 0)
        {
            logger.LogWarning("Discord role sync could not finish {Count} change(s): {Outcomes}",
                unfinished.Values.Sum(), string.Join(", ", unfinished.Select(o => $"{o.Key}={o.Value}")));
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
