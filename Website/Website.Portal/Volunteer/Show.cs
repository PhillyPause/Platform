using ManyHands.Core;
using ManyHands.Data;

namespace Website.Portal.Volunteer;

/// <summary>How the volunteer pages put an activity into words.</summary>
public static class Show
{
    public static string When(DateTimeOffset? startsAt) =>
        startsAt is DateTimeOffset s ? LocalTime.Format(s) : "Ongoing — whenever you can";

    public static string Where(string? location) =>
        string.IsNullOrWhiteSpace(location) ? "Place to be confirmed" : location;

    /// <summary>Null when there is nothing worth saying: no limit, and plenty of room.</summary>
    public static string? Room(ActivityListItem activity) => activity.SpotsRemaining switch
    {
        null => null,
        0 => "Full — sign up for the waitlist",
        1 => "One place left",
        <= 5 and var n => $"{n} places left",
        _ => null
    };

    /// <summary>A description as paragraphs, split on blank lines, never as markup.</summary>
    public static IEnumerable<string> Paragraphs(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// An organizer typed this, and it ends up in an href: anything but an
    /// absolute http or https link is refused, so a javascript: URL cannot run.
    /// </summary>
    public static string? SafeLink(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    /// <summary>"CongressionalOffice" as "Congressional office": an enum name, as a person would say it.</summary>
    public static string Words(Enum value)
    {
        var name = value.ToString();
        var words = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) words.Append(' ').Append(char.ToLowerInvariant(name[i]));
            else words.Append(name[i]);
        }
        return words.ToString();
    }

    /// <summary>A date and time on Philadelphia's clock, short: "Oct 22, 3:05 PM".</summary>
    public static string Stamp(DateTimeOffset instant) =>
        LocalTime.ToLocal(instant).ToString("MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public static string Status(SignupStatus status) => status switch
    {
        SignupStatus.Confirmed => "Signed up",
        SignupStatus.Waitlisted => "On the waitlist",
        _ => "Cancelled"
    };
}
