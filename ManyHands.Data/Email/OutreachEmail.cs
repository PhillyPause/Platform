using ManyHands.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Email;

/// <summary>A message the inbound webhook was handed.</summary>
public sealed record InboundEmail(
    string From,
    string To,
    string Subject,
    string TextBody,
    string? MessageId,
    string? InReplyTo,
    DateTimeOffset? ReceivedAt);

/// <summary>
/// Sending and receiving outreach mail: to offices, venues, partners and the
/// press. Every message is recorded before it leaves and as soon as it
/// arrives, so a thread is whole whether or not the person who started it is
/// still around.
/// </summary>
public sealed class OutreachEmail(
    CrmEmailRepository emails,
    CrmRepository crm,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<OutreachEmail> logger)
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>The address outreach goes out from and replies come back to.</summary>
    public string Mailbox => _options.OutreachAddress ?? _options.FromAddress;

    /// <summary>Sends and records. A reply continues the thread it answers; anything else starts one.</summary>
    public async Task<CrmEmail> SendAsync(
        string toAddress,
        string subject,
        string bodyText,
        Guid? organizationId,
        Guid? contactId,
        Guid? engagementId,
        Guid? conversationId,
        Guid? sentBy,
        CancellationToken ct = default)
    {
        var conversation = conversationId ?? Guid.NewGuid();
        var inReplyTo = conversationId is Guid existing
            ? await emails.GetLastInboundMessageIdAsync(existing, ct)
            : null;

        // Our own Message-ID, sent with the mail, so a reply's In-Reply-To
        // points at a row we can find.
        var messageId = $"<{Guid.NewGuid():N}@{DomainOf(Mailbox)}>";

        var record = new CrmEmail(
            Id: Guid.Empty, EmailDirection.Outbound, organizationId, contactId, engagementId,
            FromAddress: Mailbox, ToAddress: toAddress.Trim(), subject, bodyText, messageId, inReplyTo,
            conversation, EmailStatus.Queued, ProviderMessageId: null, Error: null, sentBy,
            OccurredAt: DateTimeOffset.UtcNow, CreatedAt: DateTimeOffset.UtcNow, NeedsTriage: false);

        // Recorded first: if the send throws, the attempt is still on the thread.
        var id = await emails.RecordAsync(record, ct);

        var result = await sender.SendAsync(
            new OutgoingEmail(record.ToAddress, subject, bodyText, ReplyTo: Mailbox,
                MessageId: messageId, InReplyTo: inReplyTo, From: Mailbox),
            ct);

        var status = result.Delivered
            ? (sender.IsLive ? EmailStatus.Sent : EmailStatus.Simulated)
            : EmailStatus.Failed;

        await emails.UpdateDeliveryAsync(id, status, result.ProviderMessageId, result.Error, ct);

        if (status == EmailStatus.Failed)
            logger.LogError("Outreach email failed: {Error}", result.Error);

        // On the organization's timeline too: "what have we said to this
        // office" must not need two places to look.
        if (organizationId is not null || contactId is not null)
        {
            await crm.LogInteractionAsync(organizationId, contactId, engagementId, InteractionKind.Email,
                record.OccurredAt, $"Sent: {subject}", sentBy, ct);
        }

        return record with { Id = id, Status = status, ProviderMessageId = result.ProviderMessageId };
    }

    /// <summary>
    /// Takes a message from the inbound webhook: threads it, attributes it to a
    /// known contact, and flags it for a person when it cannot. Mail from a
    /// stranger is kept, not dropped.
    /// </summary>
    public async Task<CrmEmail?> ReceiveAsync(InboundEmail message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message.From) || string.IsNullOrWhiteSpace(message.To)) return null;

        var sender = SenderAddress(message.From);
        var conversationId = await emails.FindConversationAsync(message.InReplyTo, sender, ct) ?? Guid.NewGuid();
        var match = await emails.FindContactByEmailAsync(sender, ct);

        var record = new CrmEmail(
            Id: Guid.Empty, EmailDirection.Inbound, match?.OrganizationId, match?.ContactId, EngagementId: null,
            FromAddress: message.From.Trim(), ToAddress: message.To.Trim(),
            Subject: string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject.Trim(),
            BodyText: message.TextBody ?? string.Empty,
            MessageId: string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId.Trim(),
            InReplyTo: string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo.Trim(),
            conversationId, EmailStatus.Sent, ProviderMessageId: null, Error: null, SentBy: null,
            OccurredAt: message.ReceivedAt ?? DateTimeOffset.UtcNow, CreatedAt: DateTimeOffset.UtcNow,
            // An unrecognised sender waits for someone to say who they are.
            NeedsTriage: match is null);

        var id = await emails.RecordAsync(record, ct);

        if (match is not null)
        {
            await crm.LogInteractionAsync(match.OrganizationId, match.ContactId, null, InteractionKind.Email,
                record.OccurredAt, $"Received: {record.Subject}", null, ct);
        }

        return record with { Id = id };
    }

    /// <summary>Strips a display name: "Jamie Lee &lt;jamie@x.test&gt;" becomes "jamie@x.test".</summary>
    public static string SenderAddress(string from)
    {
        var open = from.LastIndexOf('<');
        var close = from.LastIndexOf('>');
        return open >= 0 && close > open ? from[(open + 1)..close].Trim() : from.Trim();
    }

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..].Trim('>', ' ') : "localhost";
    }
}
