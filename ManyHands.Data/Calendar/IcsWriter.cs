using System.Globalization;
using System.Text;

namespace ManyHands.Data.Calendar;

/// <summary>
/// One event in a feed. Both feeds project onto this shape so the writer never
/// has to know whether it is rendering the whole programme or one person's
/// commitments.
/// </summary>
/// <param name="Uid">
/// Stable identity for the event, forever: the activity id for the public feed, the
/// signup id for a personal one. Calendar clients update an existing event when
/// the UID repeats and create a duplicate when it does not, so this may never be
/// regenerated per request.
/// </param>
/// <param name="IsTentative">A waitlisted place — a real commitment, but not a seat yet.</param>
public sealed record CalendarEntry(
    Guid Uid,
    string Title,
    string? Description,
    string? Location,
    string? MeetingUrl,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsTentative);

/// <summary>
/// RFC 5545 iCalendar text. Pure: no database, no HTTP, and no clock unless one
/// is handed in — which is what makes the awkward parts (folding, escaping, UTC
/// conversion) testable without a fixture.
/// </summary>
public static class IcsWriter
{
    /// <summary>
    /// Right-hand side of every UID. RFC 5545 wants a globally unique value, and
    /// a domain suffix is the conventional way to get one. Changing this string
    /// orphans every event already sitting in a subscriber's calendar, so it is
    /// a constant rather than configuration.
    /// </summary>
    public const string UidDomain = "volunteer.phillypause.org";

    public const string ProductId = "-//PhillyPause//ManyHands//EN";

    /// <summary>RFC 5545 §3.1: content lines are folded at 75 octets.</summary>
    private const int MaxLineOctets = 75;

    private const string Crlf = "\r\n";

    public static string Write(
        string calendarName,
        IEnumerable<CalendarEntry> entries,
        DateTimeOffset? stamp = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // One DTSTAMP for the whole document: it records when this rendering was
        // produced, not when each event was last edited.
        var dtstamp = FormatUtc(stamp ?? DateTimeOffset.UtcNow);

        var ics = new StringBuilder();

        AppendLine(ics, "BEGIN:VCALENDAR");
        AppendLine(ics, "VERSION:2.0");
        AppendLine(ics, $"PRODID:{ProductId}");
        AppendLine(ics, "CALSCALE:GREGORIAN");
        AppendLine(ics, "METHOD:PUBLISH");
        AppendText(ics, "X-WR-CALNAME", calendarName);

        foreach (var entry in entries)
        {
            AppendLine(ics, "BEGIN:VEVENT");
            AppendLine(ics, $"UID:{entry.Uid:D}@{UidDomain}");
            AppendLine(ics, $"DTSTAMP:{dtstamp}");
            AppendLine(ics, $"DTSTART:{FormatUtc(entry.StartsAt)}");
            AppendLine(ics, $"DTEND:{FormatUtc(entry.EndsAt)}");
            AppendText(ics, "SUMMARY", entry.Title);

            var description = Describe(entry);
            if (description.Length > 0) AppendText(ics, "DESCRIPTION", description);

            if (!string.IsNullOrWhiteSpace(entry.Location))
                AppendText(ics, "LOCATION", entry.Location);

            // URL takes a URI value, not TEXT (RFC 5545 §3.3.13). TEXT escaping
            // here would turn the commas and semicolons of a query string into
            // backslash sequences and break the link.
            if (!string.IsNullOrWhiteSpace(entry.MeetingUrl))
                AppendLine(ics, $"URL:{StripBreaks(entry.MeetingUrl)}");

            AppendLine(ics, entry.IsTentative ? "STATUS:TENTATIVE" : "STATUS:CONFIRMED");
            AppendLine(ics, "END:VEVENT");
        }

        AppendLine(ics, "END:VCALENDAR");
        return ics.ToString();
    }

    /// <summary>
    /// timestamptz is an instant, and a feed carries no VTIMEZONE, so every
    /// stamp goes out in UTC form. Clients render it in the reader's own zone,
    /// which is right for a calendar read on any device.
    /// </summary>
    internal static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// RFC 5545 §3.3.11: TEXT escapes backslash, semicolon, comma and newline.
    /// Colon is deliberately not escaped — it is only special in property names.
    /// </summary>
    internal static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case ';':
                    escaped.Append("\\;");
                    break;
                case ',':
                    escaped.Append("\\,");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\r':
                    escaped.Append("\\n");
                    // A CRLF pair is one line break, not two.
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    break;
                default:
                    escaped.Append(value[i]);
                    break;
            }
        }

        return escaped.ToString();
    }

    private static string Describe(CalendarEntry entry)
    {
        var lines = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(entry.Description))
            lines.Add(entry.Description.Trim());


        // Repeated in the body because plenty of clients show DESCRIPTION and
        // nothing else — a meeting link only in URL is a link nobody clicks.
        if (!string.IsNullOrWhiteSpace(entry.MeetingUrl))
            lines.Add($"Join: {entry.MeetingUrl.Trim()}");

        return string.Join("\n", lines);
    }

    private static void AppendText(StringBuilder ics, string name, string value)
        => AppendLine(ics, $"{name}:{Escape(value)}");

    private static string StripBreaks(string value) =>
        value.Trim()
             .Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Writes one content line, folded to 75 octets. Octets, not characters:
    /// the limit is on the UTF-8 encoding, and splitting a multi-byte character
    /// across a fold produces mojibake in every client that unfolds it.
    /// </summary>
    private static void AppendLine(StringBuilder ics, string line)
    {
        var limit = MaxLineOctets;
        var index = 0;

        while (true)
        {
            var start = index;
            var octets = 0;

            while (index < line.Length)
            {
                // A surrogate pair is one character and has to move together.
                var charCount = char.IsHighSurrogate(line[index])
                                && index + 1 < line.Length
                                && char.IsLowSurrogate(line[index + 1])
                    ? 2
                    : 1;

                var width = Encoding.UTF8.GetByteCount(line.AsSpan(index, charCount));
                if (octets + width > limit) break;

                octets += width;
                index += charCount;
            }

            ics.Append(line, start, index - start).Append(Crlf);

            if (index >= line.Length) return;

            // A continuation line begins with one space, and that space counts
            // against the 75 octets.
            ics.Append(' ');
            limit = MaxLineOctets - 1;
        }
    }
}
