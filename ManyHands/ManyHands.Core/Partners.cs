namespace ManyHands.Core;

// Partner outreach, from the Outreach Kit: organisations we ask to share
// PhillyPause with their members. One light ask per email, one follow-up
// after seven days, then stop; partners share our link, and we never import
// anyone's list. A partner is a CRM organization with an outreach status.

/// <summary>Where outreach to a partner stands. The kit's sheet, plus where "follow up once, then stop" ends.</summary>
public enum OutreachStatus
{
    NotContacted = 0,
    Contacted = 1,
    FollowedUp = 2,
    Replied = 3,
    Sharing = 4,
    CoHosting = 5,
    Declined = 6,
    /// <summary>No reply a week after the one follow-up: we stop, as the kit says.</summary>
    NoReply = 7
}

/// <summary>Who a partner is to us, which picks the email template that fits them.</summary>
public enum Audience
{
    /// <summary>AI-risk, EA and rationalist groups.</summary>
    AiRisk = 0,
    /// <summary>Unions and professional guilds.</summary>
    Labor = 1,
    /// <summary>Campus clubs and academic centres.</summary>
    Campus = 2,
    /// <summary>Libraries, faith communities and civic groups.</summary>
    Civic = 3,
    /// <summary>Parent groups.</summary>
    Parents = 4
}

/// <summary>The ask ladder: one rung at a time, the highest a partner has said yes to.</summary>
public enum AskRung
{
    Share = 0,
    Table = 1,
    Speaker = 2,
    CoHost = 3,
    SharedAction = 4
}

/// <summary>One row of the partners grid.</summary>
public sealed record PartnerSummary(
    Guid Id,
    string Name,
    string? Priority,
    OutreachStatus OutreachStatus,
    Audience? Audience,
    string? Category,
    string? TrackingCode,
    int Members,
    int ConfirmedMembers,
    DateTimeOffset? FirstContactedAt,
    DateTimeOffset? LastTouchAt);

/// <summary>A partner's outreach details, on its organization page.</summary>
public sealed record PartnerDetails(
    Guid Id,
    string? Priority,
    OutreachStatus? OutreachStatus,
    Audience? Audience,
    AskRung? AskRung,
    string? Category,
    string? TrackingCode,
    string? ContactChannel,
    string? FoundVia,
    string? WhyTheyFit,
    DateTimeOffset? FirstContactedAt,
    DateTimeOffset? FollowedUpAt,
    DateTimeOffset? RepliedAt);

/// <summary>How two organisations know each other, seen from one of them.</summary>
public sealed record OrganizationLink(
    Guid Id,
    bool Outgoing,
    string Connection,
    Guid? OtherId,
    string OtherName,
    string? SourceUrl);

/// <summary>Who joined through a partner's link.</summary>
public sealed record ReferredMember(Guid Id, string DisplayName, DateTimeOffset JoinedAt, bool Confirmed);

/// <summary>The path to 1,000 confirmed members, by where they came from.</summary>
public sealed record MemberGoal(int Confirmed, int ViaPartners, int ViaOtherCodes, int NoCode)
{
    public const int Target = 1000;
}

/// <summary>
/// Organisations PhillyPause never contacts. PauseAI US dropped its local
/// groups, PhillyPause among them, and there is no relationship to maintain
/// (CLAUDE.md). Nothing imports, suggests or emails them.
/// </summary>
public static class NeverContact
{
    private static readonly string[] Names = ["PauseAI US", "PauseAI-US", "Pause AI US"];
    private static readonly string[] Hosts = ["pauseai-us.org"];

    public static bool Matches(string? name, string? website = null)
    {
        if (name is not null && Names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return true;

        return website is not null
               && Uri.TryCreate(website.Trim(), UriKind.Absolute, out var uri)
               && Hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
                                 || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The words organizers see for outreach, on the pages and in the files alike.</summary>
public static class PartnerText
{
    /// <summary>How long the kit waits before its one follow-up, and again before stopping.</summary>
    public static readonly TimeSpan FollowUpAfter = TimeSpan.FromDays(7);

    public static string Status(OutreachStatus status) => status switch
    {
        OutreachStatus.NotContacted => "Not contacted",
        OutreachStatus.Contacted => "Contacted",
        OutreachStatus.FollowedUp => "Followed up",
        OutreachStatus.Replied => "Replied",
        OutreachStatus.Sharing => "Sharing",
        OutreachStatus.CoHosting => "Co-hosting",
        OutreachStatus.Declined => "Declined",
        _ => "No reply"
    };

    public static string Audience(Audience audience) => audience switch
    {
        Core.Audience.AiRisk => "AI risk",
        Core.Audience.Labor => "Unions and guilds",
        Core.Audience.Campus => "Campus",
        Core.Audience.Civic => "Libraries, faith and civic",
        _ => "Parents"
    };

    public static string Rung(AskRung rung) => rung switch
    {
        AskRung.Share => "Share our link",
        AskRung.Table => "Table at their event",
        AskRung.Speaker => "A speaker slot",
        AskRung.CoHost => "Co-host an event",
        _ => "A shared action"
    };

    /// <summary>What to do next, the kit's way: one email, one follow-up a week later, then stop.</summary>
    public static string NextStep(OutreachStatus status, DateTimeOffset? firstContactedAt, DateTimeOffset now) => status switch
    {
        OutreachStatus.NotContacted => "Write the first email",
        OutreachStatus.Contacted when firstContactedAt is { } at && now - at >= FollowUpAfter => "Follow up now",
        OutreachStatus.Contacted when firstContactedAt is { } at =>
            $"Follow up {(at + FollowUpAfter).ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture)}",
        OutreachStatus.Contacted => "Follow up a week after writing",
        OutreachStatus.FollowedUp => "Wait for a reply",
        OutreachStatus.Replied => "Answer them",
        OutreachStatus.Sharing => "Ask for the next rung",
        OutreachStatus.CoHosting => "Plan the event",
        _ => string.Empty
    };
}

/// <summary>Turning the Partner Tracker sheet's words into ours.</summary>
public static class PartnerSheet
{
    public static OutreachStatus? Status(string? label) => label?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "not contacted" => OutreachStatus.NotContacted,
        "contacted" => OutreachStatus.Contacted,
        "followed up" => OutreachStatus.FollowedUp,
        "replied" => OutreachStatus.Replied,
        "sharing / yes" or "sharing" or "yes" => OutreachStatus.Sharing,
        "co-hosting" or "cohosting" => OutreachStatus.CoHosting,
        "declined" => OutreachStatus.Declined,
        "no reply" => OutreachStatus.NoReply,
        _ => null
    };

    public static string? Priority(string? label) =>
        label?.Trim().ToUpperInvariant() is "A" or "B" or "C" ? label.Trim().ToUpperInvariant() : null;

    /// <summary>Which template group a sheet category falls in; organizers can change it after.</summary>
    public static Audience AudienceFor(string? category)
    {
        var c = category?.ToLowerInvariant() ?? string.Empty;
        if (c.Contains("parent")) return Audience.Parents;
        if (c.Contains("ai safety") || c == "ea" || c.Contains("rationalist") || c.Contains("responsible tech"))
            return Audience.AiRisk;
        if (c.Contains("campus") || c.Contains("academic") || c.Contains("education")) return Audience.Campus;
        if (c.Contains("labor") || c.Contains("professional") || c.Contains("creative") || c.Contains("journalism"))
            return Audience.Labor;
        return Audience.Civic;
    }

    public static OrganizationKind KindFor(string? category)
    {
        var c = category?.ToLowerInvariant() ?? string.Empty;
        if (c.Contains("campus")) return OrganizationKind.CampusGroup;
        if (c.StartsWith("venue", StringComparison.Ordinal)) return OrganizationKind.Venue;
        if (c.StartsWith("media", StringComparison.Ordinal)) return OrganizationKind.MediaOutlet;
        return OrganizationKind.PartnerOrg;
    }

    /// <summary>"?src=newsguild", "src=newsguild" or "newsguild": the code, or null.</summary>
    public static string? Code(string? value)
    {
        var v = value?.Trim() ?? string.Empty;
        var at = v.IndexOf("src=", StringComparison.OrdinalIgnoreCase);
        return TrackingCode.Normalize(at >= 0 ? v[(at + 4)..] : v);
    }
}

/// <summary>
/// Rows of text pasted from a spreadsheet or saved as CSV: tab-separated when
/// copied from a sheet, comma-separated from a file, quotes either way.
/// </summary>
public static class DelimitedText
{
    public static IReadOnlyList<string[]> Parse(string? text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrWhiteSpace(text)) return rows;

        var end = text.IndexOfAny(['\r', '\n']);
        var firstLine = end < 0 ? text : text[..end];
        var separator = firstLine.Contains('\t') ? '\t' : ',';

        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Exists(v => v.Length > 0)) rows.Add([.. row]);
                row.Clear();
            }
            else cell.Append(c);
        }

        row.Add(cell.ToString());
        if (row.Exists(v => v.Length > 0)) rows.Add([.. row]);
        return rows;
    }
}
