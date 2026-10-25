namespace Website.Portal.Volunteer;

/// <summary>
/// Two letters for each interest, so the members grid shows what someone
/// likes doing in a column that never cuts off; the full label is the
/// tooltip. Keyed by the activity type's slug, which does not change when a
/// label is reworded. A type added without a code here falls back to its
/// initials, and a test fails until it gets one of its own.
/// </summary>
public static class InterestCodes
{
    public static IReadOnlyDictionary<string, string> BySlug { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tabling"] = "TB",
        ["flyering"] = "FL",
        ["calling"] = "CC",
        ["letter-writing"] = "LW",
        ["lobby-meetings"] = "LC",
        ["event-support"] = "ES",
        ["protests"] = "PR",
        ["social-media"] = "SM",
        ["research"] = "RW",
        ["tech-ops"] = "WT",
        ["design"] = "DM",
        ["outreach"] = "CO",
    };

    public static string For(string slug, string label) =>
        BySlug.TryGetValue(slug, out var code) ? code : FromLabel(label);

    /// <summary>The first letters of the label's first two words, or its first two letters.</summary>
    public static string FromLabel(string label)
    {
        var words = new string([.. label.Select(c => char.IsLetter(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var code = words.Length >= 2 ? $"{words[0][0]}{words[1][0]}"
            : words.Length == 1 ? words[0][..Math.Min(2, words[0].Length)]
            : "??";
        return code.ToUpperInvariant();
    }
}
