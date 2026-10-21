namespace ManyHands.Data.Discord;

/// <summary>
/// The <c>Discord</c> configuration section. The token is the switch: with
/// one the gateway connects, without one every Discord call is a no-op that
/// logs what it would have sent, so development and CI need no bot.
/// </summary>
public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    /// <summary>Never committed: user secrets locally, an App Platform env var in production.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Says this environment must have a working bot. Production sets it, so a
    /// missing or misspelled token fails at startup instead of quietly turning
    /// announcements off for a week.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>PhillyPause's Discord server.</summary>
    public ulong? GuildId { get; set; }

    /// <summary>
    /// Where announcements go. Unset, the server's system channel, or the first
    /// channel the bot may post in.
    /// </summary>
    public ulong? AnnouncementChannelId { get; set; }

    /// <summary>
    /// The role every linked volunteer is given, so the server knows who has
    /// joined. Unset, nobody's roles are touched.
    /// </summary>
    public ulong? RoleId { get; set; }

    /// <summary>
    /// The pause between one outbound call and the next when sending in bulk:
    /// interest DMs and role edits. Discord rate-limits a burst hard, and
    /// silently — half the people get their ping and the rest are dropped.
    /// </summary>
    public TimeSpan SendDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often roles are reconciled. A sweep, not a trigger: roles drift for
    /// reasons ManyHands never sees, and only a sweep converges on all of them.
    /// </summary>
    public TimeSpan RoleSyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    public bool HasToken => !string.IsNullOrWhiteSpace(BotToken);
}
