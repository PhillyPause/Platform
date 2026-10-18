using System.Net;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Discord;

/// <summary>
/// Owns the one Discord connection and is the live <see cref="IDiscordSender"/>.
/// Registered only with a bot token; without one this is never constructed.
///
/// Nothing here may take the host down. A bad token, an outage, or a server
/// the bot was removed from produces a log line and a false, never an
/// unhandled exception: the outbox and the reminders keep running whatever
/// Discord is doing.
/// </summary>
public sealed class DiscordGateway : BackgroundService, IDiscordSender
{
    /// <summary>
    /// Guilds only. No message content, no member lists, so no privileged
    /// intent and no approval gate on the Discord application.
    /// </summary>
    private const GatewayIntents Intents = GatewayIntents.Guilds;

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly DiscordSocketClient _client;
    private readonly DiscordOptions _options;
    private readonly DiscordCommands _commands;
    private readonly ILogger<DiscordGateway> _logger;

    private CancellationToken _stopping = CancellationToken.None;
    private volatile bool _isReady;

    public DiscordGateway(IOptions<DiscordOptions> options, DiscordCommands commands, ILogger<DiscordGateway> logger)
    {
        _options = options.Value;
        _commands = commands;
        _logger = logger;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = Intents,
            AlwaysDownloadUsers = false,
            MessageCacheSize = 0,
            LogGatewayIntentWarnings = false
        });

        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.Disconnected += OnDisconnectedAsync;
        _client.SlashCommandExecuted += _commands.HandleAsync;
    }

    public bool IsLive => true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _client.LoginAsync(TokenType.Bot, _options.BotToken);
                await _client.StartAsync();
                attempt = 0;

                // Discord.Net reconnects and resumes on its own once started;
                // this loop is for the one failure it cannot handle: login.
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _isReady = false;
                var delay = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(++attempt, 8)) * 5));
                _logger.LogError(ex, "Discord connect failed (attempt {Attempt}); retrying in {Delay}", attempt, delay);
                await SafeStopClientAsync();

                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _isReady = false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await SafeStopClientAsync();
    }

    /// <summary>
    /// Posts the announcement and opens a public thread on the post itself, so
    /// it appears attached: the announcement is the top of the conversation.
    /// </summary>
    public async Task<DiscordAnnouncement> AnnounceWithThreadAsync(string message, string threadName, CancellationToken ct = default)
    {
        if (Guild() is not { } guild || AnnouncementChannel(guild) is not { } channel)
        {
            _logger.LogWarning("No Discord channel to announce in: check Discord:GuildId and the bot's permissions");
            return DiscordAnnouncement.NotDelivered;
        }

        RestUserMessage posted;
        try
        {
            posted = await channel.SendMessageAsync(DiscordMessages.Fit(message),
                allowedMentions: AllowedMentions.None, options: Request(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Announcing to Discord failed");
            return DiscordAnnouncement.NotDelivered;
        }

        try
        {
            var thread = await channel.CreateThreadAsync(
                DiscordMessages.Fit(threadName, DiscordMessages.MaxThreadNameLength),
                ThreadType.PublicThread, ThreadArchiveDuration.OneWeek, message: posted, options: Request(ct));
            return new DiscordAnnouncement(true, channel.Id, posted.Id, thread.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The announcement is out. Failing now would post it again on the
            // retry, to win back a thread: a bad trade.
            _logger.LogWarning(ex, "Announced to Discord but could not open a thread");
            return new DiscordAnnouncement(true, channel.Id, posted.Id, null);
        }
    }

    public async Task<bool> PostToThreadAsync(ulong threadId, string message, CancellationToken ct = default)
    {
        if (!_isReady) return false;

        try
        {
            // The socket cache holds threads seen this session; REST covers an
            // older one, and one since archived.
            var channel = _client.GetChannel(threadId) as IMessageChannel
                          ?? await _client.Rest.GetChannelAsync(threadId, Request(ct)) as IMessageChannel;
            if (channel is null) return false;

            await channel.SendMessageAsync(DiscordMessages.Fit(message), allowedMentions: AllowedMentions.None, options: Request(ct));
            return true;
        }
        catch (HttpException ex) when (ex.DiscordCode is DiscordErrorCode.UnknownChannel or DiscordErrorCode.ThreadIsLocked
                                       || ex.HttpCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Posting to Discord thread {ThreadId} failed", threadId);
            return false;
        }
    }

    public async Task<bool> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken ct = default)
    {
        if (!_isReady) return false;

        try
        {
            IUser? user = _client.GetUser(discordUserId);
            user ??= await _client.Rest.GetUserAsync(discordUserId, Request(ct));
            if (user is null) return false;

            await user.SendMessageAsync(DiscordMessages.Fit(message), allowedMentions: AllowedMentions.None, options: Request(ct));
            return true;
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            // Their setting, not our failure. Email stays the channel of record.
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DM to Discord user {DiscordUserId} failed", discordUserId);
            return false;
        }
    }

    /// <summary>
    /// Every way this goes wrong is an ordinary state of the world — no Manage
    /// Roles, a deleted role, someone who left — so each is an outcome the sweep
    /// can reason about, not an exception.
    /// </summary>
    public async Task<RoleSyncOutcome> SyncRoleAsync(ulong roleId, ulong discordUserId, bool shouldHaveRole, CancellationToken ct = default)
    {
        if (Guild() is not { } guild) return RoleSyncOutcome.GuildUnavailable;

        var me = guild.CurrentUser;
        if (me is null || !me.GuildPermissions.ManageRoles) return RoleSyncOutcome.Forbidden;

        var role = guild.GetRole(roleId);
        if (role is null) return RoleSyncOutcome.RoleMissing;

        // Discord refuses to touch a role at or above the bot's own highest.
        if (role.Position >= me.Hierarchy) return RoleSyncOutcome.Forbidden;

        try
        {
            // Fetching one member needs no privileged intent; listing them all would.
            IGuildUser? member = guild.GetUser(discordUserId);
            member ??= await _client.Rest.GetGuildUserAsync(guild.Id, discordUserId, Request(ct));
            if (member is null) return RoleSyncOutcome.NotInGuild;

            if (member.RoleIds.Contains(roleId) == shouldHaveRole) return RoleSyncOutcome.AlreadyCorrect;

            if (shouldHaveRole) await member.AddRoleAsync(roleId, Request(ct));
            else await member.RemoveRoleAsync(roleId, Request(ct));
            return RoleSyncOutcome.Applied;
        }
        catch (HttpException ex) when (ex.DiscordCode is DiscordErrorCode.UnknownMember or DiscordErrorCode.UnknownUser
                                       || ex.HttpCode == HttpStatusCode.NotFound)
        {
            return RoleSyncOutcome.NotInGuild;
        }
        catch (HttpException ex) when (ex.DiscordCode is DiscordErrorCode.UnknownRole)
        {
            return RoleSyncOutcome.RoleMissing;
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            return RoleSyncOutcome.Forbidden;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Syncing role {RoleId} for {DiscordUserId} failed", roleId, discordUserId);
            return RoleSyncOutcome.Failed;
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }

    private SocketGuild? Guild() =>
        _isReady && _options.GuildId is ulong id ? _client.GetGuild(id) : null;

    /// <summary>The configured channel, else the system channel, else the first the bot may post in.</summary>
    private SocketTextChannel? AnnouncementChannel(SocketGuild guild)
    {
        var me = guild.CurrentUser;
        if (me is null) return null;

        bool CanPost(SocketTextChannel? channel) => channel is not null && me.GetPermissions(channel).SendMessages;

        if (_options.AnnouncementChannelId is ulong configured && guild.GetTextChannel(configured) is { } chosen && CanPost(chosen))
            return chosen;

        return CanPost(guild.SystemChannel)
            ? guild.SystemChannel
            : guild.TextChannels.Where(CanPost).OrderBy(c => c.Position).FirstOrDefault();
    }

    private async Task OnReadyAsync()
    {
        _isReady = true;
        _logger.LogInformation("Discord ready as {BotUser}", _client.CurrentUser?.Username);

        if (Guild() is not { } guild)
        {
            _logger.LogWarning("The bot is not in the server Discord:GuildId names; it can do nothing until it is invited");
            return;
        }

        try
        {
            await _commands.RegisterAsync(guild, _stopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Announcements and DMs work without slash commands.
            _logger.LogError(ex, "Registering slash commands failed");
        }
    }

    private Task OnDisconnectedAsync(Exception? ex)
    {
        _isReady = false;
        _logger.LogWarning(ex, "Discord disconnected; the client will reconnect");
        return Task.CompletedTask;
    }

    private Task OnLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace
        };

        _logger.Log(level, message.Exception, "Discord [{Source}] {DiscordMessage}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private async Task SafeStopClientAsync()
    {
        try
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Closing the Discord connection failed");
        }
    }

    private static RequestOptions Request(CancellationToken ct) => new() { CancelToken = ct };
}
