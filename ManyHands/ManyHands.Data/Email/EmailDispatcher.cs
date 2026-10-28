using ManyHands.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Email;

/// <summary>
/// The one road out for a volunteer's email: record it, honour suppression,
/// send, record the outcome. Nothing else calls IEmailSender directly, so every
/// message ever sent is in email_messages.
/// </summary>
public sealed class EmailDispatcher(
    IEmailSender sender,
    EmailMessageRepository messages,
    IOptions<EmailOptions> options,
    ILogger<EmailDispatcher> logger)
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>See the flood check in <see cref="SendRawAsync"/>.</summary>
    internal const int FloodCap = 10;
    internal static readonly TimeSpan FloodWindow = TimeSpan.FromHours(1);

    public string PublicBaseUrl => _options.PublicBaseUrl.TrimEnd('/');

    /// <summary>An absolute link to one of <see cref="ManyHandsPaths"/>.</summary>
    public string Link(string path) => $"{PublicBaseUrl}{path}";

    public string UnsubscribeUrl(Guid commsKey) => Link(ManyHandsPaths.Unsubscribe(commsKey));

    public async Task<EmailStatus> SendAsync(
        Volunteer volunteer,
        string subject,
        string body,
        string kind,
        bool bypassOptIn = false,
        string? html = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(volunteer.Email) || volunteer.IsDeleted)
            return EmailStatus.Suppressed;

        // Mail a volunteer explicitly asked for (a sign-in link) still goes out
        // after an unsubscribe; everything else does not.
        if (!bypassOptIn && !volunteer.CanReceiveEmail)
        {
            logger.LogInformation("Suppressed {Kind} for volunteer {VolunteerId} (opted out)",
                kind, volunteer.Id);
            return EmailStatus.Suppressed;
        }

        return await SendRawAsync(
            volunteer.Id, volunteer.Email, subject, body, kind,
            unsubscribeUrl: bypassOptIn ? null : Link(ManyHandsPaths.OneClickUnsubscribe(volunteer.CommsKey)),
            html, ct);
    }

    /// <param name="html">
    /// An HTML version to send alongside <paramref name="body"/>. Only the text
    /// is recorded: it says the same thing.
    /// </param>
    public async Task<EmailStatus> SendRawAsync(
        Guid? volunteerId,
        string toAddress,
        string subject,
        string body,
        string kind,
        string? unsubscribeUrl = null,
        string? html = null,
        CancellationToken ct = default)
    {
        // Defence in depth behind the per-IP rate limits: no address receives
        // more than a handful of messages an hour, however many activities a
        // hostile script signs it up for. Real volunteers never hit this, and
        // suppressed rows are still recorded, so continued abuse cannot reset
        // the window.
        if (await messages.CountRecentAsync(toAddress, FloodWindow, ct) >= FloodCap)
        {
            var suppressedId = await messages.QueueAsync(volunteerId, toAddress, subject, body, kind, ct);
            await messages.MarkSentAsync(
                suppressedId, EmailStatus.Suppressed, null,
                $"flood cap: more than {FloodCap} messages in {FloodWindow.TotalMinutes:0} minutes", ct);
            logger.LogWarning("Email {Kind} to a capped address suppressed", kind);
            return EmailStatus.Suppressed;
        }

        var messageId = await messages.QueueAsync(volunteerId, toAddress, subject, body, kind, ct);

        var result = await sender.SendAsync(
            new OutgoingEmail(toAddress, subject, body,
                ReplyTo: _options.ReplyToAddress,
                ListUnsubscribeUrl: unsubscribeUrl,
                BodyHtml: html),
            ct);

        var status = result.Delivered
            ? (sender.IsLive ? EmailStatus.Sent : EmailStatus.Simulated)
            : EmailStatus.Failed;

        await messages.MarkSentAsync(messageId, status, result.ProviderMessageId, result.Error, ct);

        if (status == EmailStatus.Failed)
            logger.LogError("Email {Kind} failed: {Error}", kind, result.Error);

        return status;
    }
}
