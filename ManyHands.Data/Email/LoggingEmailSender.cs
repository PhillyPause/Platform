using Microsoft.Extensions.Logging;

namespace ManyHands.Data.Email;

/// <summary>
/// Default sender: records the message and sends nothing. Local dev, CI, and
/// contributor machines need no Resend account and no secrets — the whole
/// signup and magic-link flow is exercisable offline. Magic links are logged
/// so a developer can complete a sign-in from the console.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public bool IsLive => false;

    public Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[email:simulated] to={Recipient} subject={Subject}\n{Body}",
            email.ToAddress, email.Subject, email.BodyText);

        return Task.FromResult(EmailSendResult.Success(id: null));
    }
}
