using System.Text.Json;
using ManyHands.Core;
using ManyHands.Data.Email;
using Microsoft.Extensions.Logging;

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
    EmailDispatcher email,
    ILogger<OutboxDispatcher> logger)
{
    private string ManageUrl => email.Link(ManyHandsPaths.Me);

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
