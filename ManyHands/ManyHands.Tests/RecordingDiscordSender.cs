using System.Collections.Concurrent;
using ManyHands.Data.Discord;

namespace ManyHands.Tests;

/// <summary>
/// A live-looking Discord that keeps what it was asked to send. Set
/// <see cref="Deliver"/> false to play an outage.
/// </summary>
public sealed class RecordingDiscordSender : IDiscordSender
{
    public const ulong ChannelId = 900_000_000_000_000_001;
    public const ulong MessageId = 900_000_000_000_000_002;
    public const ulong ThreadId = 900_000_000_000_000_003;

    public bool Deliver { get; set; } = true;

    /// <summary>What <see cref="SyncRoleAsync"/> answers.</summary>
    public RoleSyncOutcome RoleOutcome { get; set; } = RoleSyncOutcome.Applied;

    public ConcurrentQueue<string> Announcements { get; } = new();
    public ConcurrentQueue<(ulong ThreadId, string Message)> ThreadPosts { get; } = new();
    public ConcurrentQueue<(ulong DiscordUserId, string Message)> DirectMessages { get; } = new();
    public ConcurrentQueue<(ulong RoleId, ulong DiscordUserId, bool Grant)> RoleChanges { get; } = new();

    public bool IsLive => true;

    public Task<DiscordAnnouncement> AnnounceWithThreadAsync(string message, string threadName, CancellationToken ct = default)
    {
        if (!Deliver) return Task.FromResult(DiscordAnnouncement.NotDelivered);
        Announcements.Enqueue(message);
        return Task.FromResult(new DiscordAnnouncement(true, ChannelId, MessageId, ThreadId));
    }

    public Task<bool> PostToThreadAsync(ulong threadId, string message, CancellationToken ct = default)
    {
        if (Deliver) ThreadPosts.Enqueue((threadId, message));
        return Task.FromResult(Deliver);
    }

    public Task<bool> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken ct = default)
    {
        if (Deliver) DirectMessages.Enqueue((discordUserId, message));
        return Task.FromResult(Deliver);
    }

    public Task<RoleSyncOutcome> SyncRoleAsync(ulong roleId, ulong discordUserId, bool shouldHaveRole, CancellationToken ct = default)
    {
        RoleChanges.Enqueue((roleId, discordUserId, shouldHaveRole));
        return Task.FromResult(RoleOutcome);
    }
}
