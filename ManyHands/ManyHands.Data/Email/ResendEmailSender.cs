using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Email;

/// <summary>
/// Resend via its REST API through a typed HttpClient.
/// Deliberately no vendor SDK: one POST is not worth a dependency whose major
/// versions we would have to track.
/// </summary>
public sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsLive => true;

    public async Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var fromAddress = email.From ?? _options.FromAddress;
        var payload = new ResendRequest(
            From: string.IsNullOrWhiteSpace(_options.FromName)
                ? fromAddress
                : $"{_options.FromName} <{fromAddress}>",
            To: [email.ToAddress],
            Subject: email.Subject,
            Text: email.BodyText,
            Html: email.BodyHtml,
            ReplyTo: email.ReplyTo ?? _options.ReplyToAddress,
            Headers: BuildHeaders(email));

        try
        {
            using var response = await http.PostAsJsonAsync("emails", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Resend rejected message to {Recipient}: {Status} {Body}",
                    Redact(email.ToAddress), (int)response.StatusCode, body);
                return EmailSendResult.Failure($"{(int)response.StatusCode}: {body}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<ResendResponse>(ct);
            return EmailSendResult.Success(parsed?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Transient: the outbox retries with backoff.
            logger.LogWarning(ex, "Resend call failed for {Recipient}", Redact(email.ToAddress));
            return EmailSendResult.Failure(ex.Message);
        }
    }

    internal static Dictionary<string, string>? BuildHeaders(OutgoingEmail email)
    {
        var headers = new Dictionary<string, string>();

        // RFC 8058: gives mail clients a native unsubscribe button, which keeps
        // complaints out of the spam-report path that wrecks sender reputation.
        if (!string.IsNullOrWhiteSpace(email.ListUnsubscribeUrl))
        {
            headers["List-Unsubscribe"] = $"<{email.ListUnsubscribeUrl}>";
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
        }

        // Threading, both ways: our Message-ID is what a reply's In-Reply-To
        // will name, and In-Reply-To/References keep our reply in the
        // recipient's thread.
        if (!string.IsNullOrWhiteSpace(email.MessageId)) headers["Message-ID"] = email.MessageId;

        if (!string.IsNullOrWhiteSpace(email.InReplyTo))
        {
            headers["In-Reply-To"] = email.InReplyTo;
            headers["References"] = email.InReplyTo;
        }

        return headers.Count == 0 ? null : headers;
    }

    /// <summary>Volunteer addresses are PII; logs get a shape, not an inbox.</summary>
    private static string Redact(string address)
    {
        var at = address.IndexOf('@');
        return at <= 1 ? "***" : $"{address[0]}***{address[at..]}";
    }

    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Html,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ReplyTo,
        [property: JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        Dictionary<string, string>? Headers);

    private sealed record ResendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
