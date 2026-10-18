using System.Text.Json;
using ManyHands.Core;
using ManyHands.Data.Discord;
using ManyHands.Data.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Workers;

/// <summary>
/// Turns outbox rows into messages volunteers actually receive.
///
/// The outbox is at-least-once, so every handler survives running twice. Where
/// a natural key exists it is used, and a redelivery finds the work done.
/// Where none exists — a Resend send offers no idempotency key — a crash
/// between sending and marking the row processed really does send a second
/// copy. Stated plainly rather than implied to be handled.
/// </summary>
public sealed class OutboxDispatcher(
    NotificationQueries notifications,
    VolunteerRepository volunteers,
    ActivityRepository activities,
    ActivityAdminRepository activityAdmin,
    ReportingRepository reporting,
    PingRepository pings,
    InviteRepository invites,
    EmailDispatcher email,
    DiscordQueries discordQueries,
    IDiscordSender discord,
    IOptions<DiscordOptions> discordOptions,
    ILogger<OutboxDispatcher> logger)
{
    private readonly DiscordOptions _discord = discordOptions.Value;

    private string ManageUrl => email.Link(ManyHandsPaths.Me);

    private string BaseUrl => email.PublicBaseUrl;

    public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        switch (message.Kind)
        {
            case OutboxKinds.SignupConfirmed:
                await HandleSignupConfirmedAsync(message, ct);
                break;

            case OutboxKinds.SignupPromoted:
                await HandlePromotedAsync(message, ct);
                break;

            case OutboxKinds.ActivityReminder:
                await HandleReminderAsync(message, ct);
                break;

            case OutboxKinds.MemberWelcome:
                await HandleWelcomeAsync(message, ct);
                break;

            case OutboxKinds.ActivityPublished:
                await HandlePublishedAsync(message, ct);
                break;

            case OutboxKinds.InviteSent:
                await HandleInviteAsync(message, ct);
                break;

            default:
                // Marked processed by the worker: an unknown kind will not
                // become known by retrying it.
                logger.LogWarning("Unknown outbox kind {Kind} (id {Id})", message.Kind, message.Id);
                break;
        }
    }

    private async Task HandleSignupConfirmedAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "signupId") is not Guid signupId) return;

        var n = await notifications.GetSignupAsync(signupId, ct);
        if (n is null) return;

        var (subject, body) = EmailTemplates.SignupConfirmation(
            n.DisplayName, n.Title, n.StartsAt, n.Location,
            waitlisted: n.Status == SignupStatus.Waitlisted,
            ManageUrl, email.UnsubscribeUrl(n.CommsKey));

        await email.SendAsync(n.AsVolunteer(), subject, body, "signup.confirmation", ct: ct);

        // Nothing past the email may fail this row: the retry would send the
        // email again. Both Discord steps are best effort.
        if (n.DiscordUserId is ulong discordUserId && !n.IsDeleted)
        {
            // A nudge beside the email, not instead of it. No idempotency key
            // exists for a DM, so a redelivery repeats it, as it does the email.
            await TryDiscordAsync(() => discord.SendDirectMessageAsync(
                discordUserId, DiscordMessages.SignupConfirmation(n, BaseUrl), ct), "signup DM", n.SignupId);
        }

        await NoteSignupInThreadAsync(n, ct);
    }

    /// <summary>
    /// Keeps the activity's thread visibly filling up. Claimed like a ping, so
    /// a redelivered row does not post the note twice.
    /// </summary>
    private async Task NoteSignupInThreadAsync(SignupNotification n, CancellationToken ct)
    {
        await TryDiscordAsync(async () =>
        {
            if (await discordQueries.GetAnnouncementThreadAsync(n.ActivityId, ct) is not ulong thread) return true;
            if (!await pings.TryClaimAsync(n.ActivityId, n.VolunteerId, PingChannel.Thread, ct)) return true;

            var delivered = await discord.PostToThreadAsync(thread, DiscordMessages.ThreadSignupNote(n.Status), ct);
            await pings.MarkDeliveredAsync(n.ActivityId, n.VolunteerId, PingChannel.Thread, delivered, ct);
            return delivered;
        }, "thread note", n.SignupId);
    }

    private async Task TryDiscordAsync(Func<Task<bool>> send, string what, Guid signupId)
    {
        try
        {
            if (!await send())
                logger.LogInformation("Discord {What} for signup {SignupId} not delivered; the email was sent", what, signupId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Discord {What} for signup {SignupId} failed; the email was sent", what, signupId);
        }
    }

    /// <summary>
    /// The waitlist kept its promise: a cancellation freed a place and this
    /// signup now holds it. A redelivery re-sends the good news, which beats
    /// never sending it.
    /// </summary>
    private async Task HandlePromotedAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "signupId") is not Guid signupId) return;

        var n = await notifications.GetSignupAsync(signupId, ct);

        // Cancelled or gone between queue and dispatch: say nothing.
        if (n is null || n.Status != SignupStatus.Confirmed) return;

        var (subject, body) = EmailTemplates.WaitlistPromoted(
            n.DisplayName, n.Title, n.StartsAt, n.Location,
            ManageUrl, email.UnsubscribeUrl(n.CommsKey));

        await email.SendAsync(n.AsVolunteer(), subject, body, "signup.promoted", ct: ct);
    }

    private async Task HandleReminderAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "signupId") is not Guid signupId) return;

        var n = await notifications.GetSignupAsync(signupId, ct);

        // Cancelled after the reminder was queued, or the time was cleared: do not nag.
        if (n?.StartsAt is not DateTimeOffset startsAt || n.Status != SignupStatus.Confirmed) return;

        var (subject, body) = EmailTemplates.ActivityReminder(
            n.DisplayName, n.Title, startsAt, n.Location, n.MeetingUrl,
            ManageUrl, email.UnsubscribeUrl(n.CommsKey));

        await email.SendAsync(n.AsVolunteer(), subject, body, "activity.reminder", ct: ct);
    }

    private async Task HandleWelcomeAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "volunteerId") is not Guid volunteerId) return;

        var volunteer = await volunteers.GetByIdAsync(volunteerId, ct);
        if (volunteer is null) return;

        var (subject, body) = EmailTemplates.Welcome(
            volunteer.DisplayName,
            activitiesUrl: email.Link(ManyHandsPaths.Activities),
            ManageUrl,
            email.UnsubscribeUrl(volunteer.CommsKey));

        await email.SendAsync(volunteer, subject, body, "member.welcome", ct: ct);
    }

    /// <summary>
    /// Announces the activity on Discord, then tells the people whose interests
    /// match: by DM if they linked Discord, by email if not, never both. Each
    /// person carries their own claim, so a redelivered row resumes where it
    /// stopped rather than reaching anyone twice, and an activity with no types
    /// pings nobody: there is no interest to match.
    /// </summary>
    private async Task HandlePublishedAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "activityId") is not Guid activityId) return;

        // Unpublished or gone between queue and dispatch: announce nothing.
        var activity = await activities.GetPublishedByIdAsync(activityId, ct);
        var draft = await activityAdmin.GetAsync(activityId, ct);
        if (activity is null || draft is null) return;

        await AnnounceAsync(activity, ct);

        // Reached whether or not this delivery is the one that announced: a row
        // redelivered after the pings half-finished must pick up the rest.
        var audience = await reporting.GetInterestedAsync(draft.TypeIds, ct);
        var dm = DiscordMessages.InterestPing(activity, BaseUrl);
        int dms = 0, emails = 0;

        foreach (var volunteer in audience.Where(v => v.MayContact))
        {
            if (volunteer.DiscordUserId is ulong discordUserId)
            {
                if (!await pings.TryClaimAsync(activityId, volunteer.Id, PingChannel.DirectMessage, ct)) continue;

                bool delivered;
                try
                {
                    delivered = await discord.SendDirectMessageAsync(discordUserId, dm, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One person's DM must not abort everyone else's.
                    logger.LogWarning(ex, "Interest DM to volunteer {VolunteerId} threw", volunteer.Id);
                    delivered = false;
                }

                await pings.MarkDeliveredAsync(activityId, volunteer.Id, PingChannel.DirectMessage, delivered, ct);
                if (delivered) dms++;

                // Discord throttles a burst of DMs hard and silently.
                if (_discord.SendDelay > TimeSpan.Zero && discord.IsLive) await Task.Delay(_discord.SendDelay, ct);
                continue;
            }

            if (!volunteer.CanReceiveEmail) continue;
            if (!await pings.TryClaimAsync(activityId, volunteer.Id, PingChannel.Email, ct)) continue;

            var (subject, body) = EmailTemplates.InterestPing(
                volunteer.DisplayName, activity.Title, activity.StartsAt, activity.Location,
                activityUrl: email.Link(ManyHandsPaths.Activity(activityId)),
                unsubscribeUrl: email.UnsubscribeUrl(volunteer.CommsKey));

            var status = await email.SendAsync(volunteer, subject, body, "interest.ping", ct: ct);
            var sent = status is EmailStatus.Sent or EmailStatus.Simulated;
            await pings.MarkDeliveredAsync(activityId, volunteer.Id, PingChannel.Email, sent, ct);
            if (sent) emails++;
        }

        logger.LogInformation(
            "Interest pings for activity {ActivityId}: {Dms} by Discord, {Emails} by email, of {Audience} interested",
            activityId, dms, emails, audience.Count);
    }

    /// <summary>
    /// The Discord post and its thread, once per activity. Discord offers no
    /// idempotency key, so the claim in <c>external_publications</c> is the
    /// guard. An undelivered post throws, leaving the claim open for the
    /// outbox to retry; the pings that follow carry their own claims.
    /// </summary>
    private async Task AnnounceAsync(ActivityListItem activity, CancellationToken ct)
    {
        if (!await discordQueries.TryClaimAnnouncementAsync(activity.Id, ct)) return;

        var posted = await discord.AnnounceWithThreadAsync(
            DiscordMessages.Announcement(activity, BaseUrl), DiscordMessages.ThreadName(activity), ct);

        if (!posted.Delivered)
        {
            var error = $"The Discord announcement for activity {activity.Id} was not delivered.";
            await discordQueries.MarkAnnouncementFailedAsync(activity.Id, error, ct);
            throw new InvalidOperationException(error);
        }

        // Closed whether it reached Discord or only the log, so a token added
        // later does not announce months-old work.
        await discordQueries.MarkAnnouncedAsync(activity.Id, posted.MessageId, posted.ThreadId,
            _discord.GuildId is ulong guild ? DiscordMessages.MessageUrl(guild, posted.ChannelId, posted.MessageId) : null, ct);
    }

    /// <summary>
    /// Mints the invite link and mails it. Minted here rather than when the
    /// organizer pressed Invite, so it exists only in the email.
    /// </summary>
    private async Task HandleInviteAsync(OutboxMessage message, CancellationToken ct)
    {
        if (ReadGuid(message.Payload, "inviteId") is not Guid inviteId) return;

        // Revoked, accepted or expired since: all normal, and nothing to send.
        if (await invites.IssueLinkAsync(inviteId, ct) is not { } issued) return;

        var (subject, body) = EmailTemplates.Invite(
            issued.Invite.InvitedByName, issued.Invite.Note,
            joinUrl: email.Link(ManyHandsPaths.AcceptInvite(issued.Token)),
            validFor: InviteRepository.Lifetime);

        // No volunteer yet, and so no unsubscribe: there is nothing to leave
        // until they accept. One unsolicited invite is what the organizer
        // vouched for by sending it.
        await email.SendRawAsync(null, issued.Invite.Email, subject, body, "invite.sent", ct: ct);
    }

    internal static Guid? ReadGuid(string payload, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                   && Guid.TryParse(value.GetString(), out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
