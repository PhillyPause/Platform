using Discord;
using Discord.WebSocket;
using ManyHands.Core;
using ManyHands.Data.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Discord;

/// <summary>
/// <c>/activities</c>, <c>/mysignups</c> and <c>/link</c>. Every reply is
/// ephemeral: a channel does not need a copy of one person's schedule, and only
/// an ephemeral reply can safely say anything about an individual.
/// </summary>
public sealed class DiscordCommands(
    DiscordQueries discord,
    ActivityRepository activities,
    SignupRepository signups,
    DiscordLinks links,
    IOptions<EmailOptions> emailOptions,
    ILogger<DiscordCommands> logger)
{
    /// <summary>Enough to be useful in a chat message, few enough to read.</summary>
    private const int ListLimit = 5;

    // The same origin every email link is built on.
    private readonly string _baseUrl = emailOptions.Value.PublicBaseUrl.TrimEnd('/');

    /// <summary>
    /// Registered on the one server rather than globally: they appear at once,
    /// where global commands take up to an hour. A bulk overwrite is idempotent,
    /// so this runs on every reconnect without accumulating duplicates.
    /// </summary>
    public async Task RegisterAsync(SocketGuild guild, CancellationToken ct = default)
    {
        ApplicationCommandProperties[] commands =
        [
            new SlashCommandBuilder().WithName("activities").WithDescription("What PhillyPause needs a hand with next").Build(),
            new SlashCommandBuilder().WithName("mysignups").WithDescription("What you have coming up").Build(),
            new SlashCommandBuilder().WithName("link").WithDescription("Link your Discord account to PhillyPause").Build()
        ];

        await guild.BulkOverwriteApplicationCommandAsync(commands, new RequestOptions { CancelToken = ct });
        logger.LogInformation("Registered {Count} slash commands", commands.Length);
    }

    public async Task HandleAsync(SocketSlashCommand command)
    {
        try
        {
            // Discord allows three seconds to acknowledge; a cold database can
            // outlast that. Defer first, then take as long as the query needs.
            await command.DeferAsync(ephemeral: true);

            var reply = command.Data.Name switch
            {
                "activities" => await ActivitiesAsync(),
                "mysignups" => await MySignupsAsync(command.User.Id),
                "link" => LinkFor(command.User),
                _ => "I do not know that command."
            };

            await command.FollowupAsync(DiscordMessages.Fit(reply), ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Slash command /{Command} failed", command.Data.Name);
            try
            {
                await command.FollowupAsync("Something went wrong looking that up. The organizers have it in the logs.", ephemeral: true);
            }
            catch (Exception followup)
            {
                logger.LogDebug(followup, "Could not deliver the failure reply for /{Command}", command.Data.Name);
            }
        }
    }

    private async Task<string> ActivitiesAsync()
    {
        var published = await activities.GetPublishedAsync();
        return DiscordMessages.List(
            header: "**What PhillyPause needs a hand with**",
            items: published.Take(ListLimit).Select(a => DiscordMessages.ActivityLine(a, _baseUrl)).ToList(),
            emptyText: "Nothing published right now. Organizers post here as soon as there is something to sign up for.");
    }

    private async Task<string> MySignupsAsync(ulong discordUserId)
    {
        if (await discord.GetVolunteerByDiscordIdAsync(discordUserId) is not { } volunteer)
            return DiscordMessages.NotLinked;

        var now = DateTimeOffset.UtcNow;
        var upcoming = (await signups.GetForVolunteerAsync(volunteer.Id))
            .Where(s => s.Status != SignupStatus.Cancelled && (s.EndsAt ?? s.StartsAt ?? now) >= now)
            .Take(ListLimit)
            .Select(s => DiscordMessages.SignupLine(s, _baseUrl))
            .ToList();

        return DiscordMessages.List(
            header: $"**{volunteer.DisplayName}, what you have coming up**",
            items: upcoming,
            emptyText: "Nothing coming up. Try `/activities` to see what needs doing.");
    }

    private string LinkFor(IUser user) =>
        DiscordMessages.LinkInstructions(
            $"{_baseUrl}{ManyHandsPaths.LinkDiscord(links.Issue(user.Id, user.GlobalName ?? user.Username))}",
            DiscordLinks.Lifetime);
}
