using System.Globalization;
using System.Text;
using ManyHands.Core;
using ManyHands.Data;

namespace Website.Portal.Volunteer;

/// <summary>The team's one download: who came to what.</summary>
public static class TeamEndpoints
{
    public const string ParticipationPath = "/team/participation.csv";

    /// <summary>Where the mail provider posts outreach replies.</summary>
    public const string InboundMailPath = "/team/mail/inbound";

    public const string InboundTokenHeader = "X-Inbound-Token";

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        // Replies to outreach mail, posted by whatever receives mail for the
        // outreach address (a Cloudflare Email Worker, or a provider's inbound
        // webhook), as JSON with the shared secret in a header. No secret
        // configured means the webhook is off, not open.
        app.MapPost(InboundMailPath, async (
            HttpContext http,
            InboundMailPayload payload,
            ManyHands.Data.Email.OutreachEmail outreach,
            Microsoft.Extensions.Options.IOptions<ManyHands.Data.Email.EmailOptions> options,
            CancellationToken ct) =>
        {
            // A machine is asking, so it gets a bare status, not the site's
            // not-found page re-run as a POST.
            if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } pages)
                pages.Enabled = false;

            if (string.IsNullOrWhiteSpace(options.Value.InboundSecret)) return Results.NotFound();
            if (!SameSecret(http.Request.Headers[InboundTokenHeader].ToString(), options.Value.InboundSecret))
                return Results.Unauthorized();

            var stored = await outreach.ReceiveAsync(new ManyHands.Data.Email.InboundEmail(
                payload.From ?? string.Empty, payload.To ?? string.Empty, payload.Subject ?? string.Empty,
                payload.Text ?? string.Empty, payload.MessageId, payload.InReplyTo, payload.ReceivedAt), ct);

            // Anything understood is a 200, so the sender does not retry it forever.
            return stored is null
                ? Results.BadRequest(new { error = "from and to are required" })
                : Results.Ok(new { stored = true, stored.ConversationId, stored.NeedsTriage });
        })
        .DisableAntiforgery();

        // CSV on purpose: every organizer can open one, and it is where any
        // report ends up anyway.
        app.MapGet(ParticipationPath, async (ReportingRepository reporting, CancellationToken ct) =>
        {
            var rows = await reporting.GetParticipationAsync(ct);
            var csv = Csv.Build(
                ["Volunteer", "Email", "Activity", "Types", "Starts (Eastern)", "Status", "Checked in"],
                rows.Select(r => new[]
                {
                    r.DisplayName, r.Email ?? string.Empty, r.Title, r.Types,
                    r.StartsAt is { } s ? LocalTime.ToLocal(s).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty,
                    r.Status.ToString(),
                    r.CheckedInAt is null ? "no" : "yes"
                }));

            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv", "participation.csv");
        })
        .RequireAuthorization(TeamPolicies.Organizer);

        return app;
    }

    /// <summary>Constant time: a shared secret must not leak through how long the comparison takes.</summary>
    internal static bool SameSecret(string presented, string expected)
    {
        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>What the inbound webhook accepts.</summary>
public sealed record InboundMailPayload(
    string? From,
    string? To,
    string? Subject,
    string? Text,
    string? MessageId,
    string? InReplyTo,
    DateTimeOffset? ReceivedAt);

public static class Csv
{
    public static string Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var row in rows) builder.AppendLine(string.Join(',', row.Select(Cell)));
        return builder.ToString();
    }

    /// <summary>
    /// Names and emails here were typed into public forms. A cell starting
    /// with =, +, - or @ is a formula to a spreadsheet, so it is defused with a
    /// leading apostrophe before an organizer opens the file.
    /// </summary>
    public static string Cell(string value)
    {
        if (value.Length == 0) return value;

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;

        var quote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return quote ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
