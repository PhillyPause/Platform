using System.Globalization;
using ManyHands.Core;

namespace ManyHands.Data;

/// <summary>
/// Turning a stored instant into words a volunteer can act on.
///
/// Everything is stored as timestamptz: an instant, no wall clock attached. An
/// email is fixed text, so format the instant raw and a 10 AM activity mails
/// out as "2:00 PM". Every email body goes through here instead, converted to
/// Philadelphia's clock and labelled, so nobody turns up four hours late.
/// </summary>
public static class LocalTime
{
    private const string Label = "Eastern time";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(Organisation.TimeZone);

    /// <summary>"Saturday, August 22 at 10:00 AM (Eastern time)".</summary>
    public static string Format(DateTimeOffset instant) =>
        Render(instant, "dddd, MMMM d 'at' h:mm tt");

    /// <summary>"Saturday at 10:00 AM (Eastern time)", for short notices.</summary>
    public static string FormatShort(DateTimeOffset instant) =>
        Render(instant, "dddd 'at' h:mm tt");

    // Invariant, so a server whose culture is not en-US still writes
    // "Saturday" and "AM".
    private static string Render(DateTimeOffset instant, string pattern) =>
        $"{TimeZoneInfo.ConvertTime(instant, Zone).ToString(pattern, CultureInfo.InvariantCulture)} ({Label})";
}
