using System.Globalization;
using System.Text;

namespace ManyHands.Core;

/// <summary>
/// An activity's readable address, the last part of
/// <c>phillypause.org/events/philly-says-pause</c>. The database makes and
/// keeps it (<c>0004_activity_slugs.sql</c>): from the title, with the date
/// added when the title's is taken, and frozen once published. This is the
/// same base rule, so the editor can show the address before it is saved.
/// </summary>
public static class ActivitySlug
{
    public const int MaxLength = 60;

    /// <summary>Lower-case letters and digits, one dash for each run of anything else, at most 60.</summary>
    public static string From(string? text)
    {
        // Accents come off first (café → cafe); the database's rule has only
        // ASCII to work with, and an accented letter would otherwise become a dash.
        var plain = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        }

        var slug = new StringBuilder();
        var dash = false;
        foreach (var c in plain.ToString().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (dash && slug.Length > 0) slug.Append('-');
                slug.Append(c);
                dash = false;
            }
            else
            {
                dash = true;
            }
        }

        var result = slug.Length > MaxLength ? slug.ToString(0, MaxLength).TrimEnd('-') : slug.ToString();
        return result.Length == 0 ? "event" : result;
    }

    /// <summary>Whether <paramref name="value"/> could be a slug at all: what a route accepts before asking the database.</summary>
    public static bool IsWellFormed(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxLength + 11 && From(value) == value;
}
