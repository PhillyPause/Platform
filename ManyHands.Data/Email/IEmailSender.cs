namespace ManyHands.Data.Email;

/// <param name="MessageId">Our own Message-ID, so a reply can be threaded back to the row that recorded it.</param>
/// <param name="InReplyTo">The message this answers, so the recipient's mail client threads it too.</param>
/// <param name="From">An address other than the default sender, for outreach mail.</param>
public sealed record OutgoingEmail(
    string ToAddress,
    string Subject,
    string BodyText,
    string? ReplyTo = null,
    string? ListUnsubscribeUrl = null,
    string? MessageId = null,
    string? InReplyTo = null,
    string? From = null);

public sealed record EmailSendResult(bool Delivered, string? ProviderMessageId, string? Error)
{
    public static EmailSendResult Success(string? id) => new(true, id, null);
    public static EmailSendResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// Provider seam. Resend in production; the logging sender
/// keeps local dev and CI working with no account and no secrets.
/// </summary>
public interface IEmailSender
{
    /// <summary>True when messages actually leave the building.</summary>
    bool IsLive { get; }

    Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
