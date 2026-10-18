using Microsoft.Extensions.Logging;

namespace ManyHands.Data.Discord;

/// <summary>
/// What came back from posting an announcement. The ids matter: the thread is
/// where later signups are noted, and the channel and message make the link
/// back to the post.
/// </summary>
/// <param name="Delivered">
/// Whether the announcement itself went out. A missing thread does not make
/// this false: the message is out, and re-announcing to win a thread would be
/// worse than living without one.
/// </param>
public sealed record DiscordAnnouncement(bool Delivered, ulong? ChannelId, ulong? MessageId, ulong? ThreadId)
{
    public static DiscordAnnouncement NotDelivered { get; } = new(false, null, null, null);
}

/// <summary>
/// How one role change ended. Each drives a different decision in the sweep:
/// a role that no longer exists is finished business; a server that is briefly
/// unreachable must be tried again.
/// </summary>
public enum RoleSyncOutcome
{
    Applied,
    AlreadyCorrect,
    /// <summary>No bot token, so nothing happened, and nothing may be recorded as if it had.</summary>
    Simulated,
    NotInGuild,
    RoleMissing,
    GuildUnavailable,
    /// <summary>No Manage Roles, or the role sits at or above the bot's own.</summary>
    Forbidden,
    Failed
}

/// <summary>
/// The one road out to Discord. Callers never ask whether a bot token is
/// configured, and failures come back as values, not exceptions: a Discord
/// outage must not stop the outbox.
/// </summary>
public interface IDiscordSender
{
    /// <summary>True when messages actually reach Discord.</summary>
    bool IsLive { get; }

    /// <summary>Posts to the announcement channel and opens a discussion thread on the post.</summary>
    Task<DiscordAnnouncement> AnnounceWithThreadAsync(string message, string threadName, CancellationToken ct = default);

    /// <summary>Posts into a thread opened earlier. False when it is gone, locked or unreachable.</summary>
    Task<bool> PostToThreadAsync(ulong threadId, string message, CancellationToken ct = default);

    /// <summary>DMs someone. False when they have DMs from server members off, which is their choice.</summary>
    Task<bool> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken ct = default);

    /// <summary>Brings one member's role in line with what ManyHands believes. Never throws.</summary>
    Task<RoleSyncOutcome> SyncRoleAsync(ulong roleId, ulong discordUserId, bool shouldHaveRole, CancellationToken ct = default);
}

/// <summary>
/// The default: writes what it would have posted and sends nothing. It reports
/// messages as delivered, so a missing token never parks an outbox row and a
/// token added later does not announce months-old work. Roles report
/// <see cref="RoleSyncOutcome.Simulated"/> instead: a role is state, not a
/// moment, and recording a grant that never happened would stop the first
/// real sweep from making it.
/// </summary>
public sealed class LoggingDiscordSender : IDiscordSender
{
    private readonly ILogger<LoggingDiscordSender> _logger;

    public LoggingDiscordSender(ILogger<LoggingDiscordSender> logger)
    {
        _logger = logger;
        _logger.LogInformation(
            "Discord bot off (no Discord:BotToken): announcements, threads, DMs and role changes go to the log only");
    }

    public bool IsLive => false;

    public Task<DiscordAnnouncement> AnnounceWithThreadAsync(string message, string threadName, CancellationToken ct = default)
    {
        _logger.LogInformation("[discord:simulated] thread=\"{ThreadName}\"\n{Message}",
            DiscordMessages.Fit(threadName, DiscordMessages.MaxThreadNameLength), DiscordMessages.Fit(message));
        return Task.FromResult(new DiscordAnnouncement(true, null, null, null));
    }

    public Task<bool> PostToThreadAsync(ulong threadId, string message, CancellationToken ct = default)
    {
        _logger.LogInformation("[discord:simulated] thread={ThreadId}\n{Message}", threadId, DiscordMessages.Fit(message));
        return Task.FromResult(true);
    }

    public Task<bool> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken ct = default)
    {
        _logger.LogInformation("[discord:simulated] dm={DiscordUserId}\n{Message}", discordUserId, DiscordMessages.Fit(message));
        return Task.FromResult(true);
    }

    public Task<RoleSyncOutcome> SyncRoleAsync(ulong roleId, ulong discordUserId, bool shouldHaveRole, CancellationToken ct = default)
    {
        _logger.LogInformation("[discord:simulated] would {Action} role {RoleId} for {DiscordUserId}",
            shouldHaveRole ? "add" : "remove", roleId, discordUserId);
        return Task.FromResult(RoleSyncOutcome.Simulated);
    }
}
