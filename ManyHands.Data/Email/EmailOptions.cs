namespace ManyHands.Data.Email;

/// <summary>The <c>Email</c> configuration section.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"log" (the default, which sends nothing) or "resend".</summary>
    public string Provider { get; set; } = "log";

    /// <summary>
    /// Must be on a dedicated mail subdomain with its own SPF, DKIM and DMARC,
    /// so app sending cannot damage the root domain's reputation.
    /// </summary>
    public string FromAddress { get; set; } = "volunteer@localhost";

    public string FromName { get; set; } = Core.Organisation.Name;

    public string? ReplyToAddress { get; set; }

    /// <summary>Resend API key. Never committed: user secrets locally, an env var in production.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Where outreach mail — to offices, venues, the press — goes out from and
    /// replies come back to. Falls back to <see cref="FromAddress"/>.
    /// </summary>
    public string? OutreachAddress { get; set; }

    /// <summary>
    /// The shared secret the inbound mail webhook expects in its
    /// <c>X-Inbound-Token</c> header. Unset means the webhook is off: it
    /// answers 404 rather than taking anonymous mail.
    /// </summary>
    public string? InboundSecret { get; set; }

    /// <summary>The public origin every link in an email is built on.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5203";

    public bool UsesResend =>
        string.Equals(Provider, "resend", StringComparison.OrdinalIgnoreCase);
}
