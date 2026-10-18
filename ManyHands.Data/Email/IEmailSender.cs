namespace ManyHands.Data.Email;

public sealed record OutgoingEmail(
    string ToAddress,
    string Subject,
    string BodyText,
    string? ReplyTo = null,
    string? ListUnsubscribeUrl = null);

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
