using System.Collections.Concurrent;
using ManyHands.Data.Email;

namespace ManyHands.Tests;

/// <summary>Keeps every message it is handed, HTML and all, and sends nothing.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public bool IsLive => false;

    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();

    public Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        Sent.Enqueue(email);
        return Task.FromResult(EmailSendResult.Success(id: null));
    }
}
