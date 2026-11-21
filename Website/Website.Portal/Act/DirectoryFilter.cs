using System.Text;
using Website.Content;

namespace Website.Portal.Act;

/// <summary>
/// The Everyone view's filter: which government, which county, and a name or
/// a district number. A list of dozens is one nobody reads, so nobody is
/// listed until something is chosen, and never more than <see cref="Shown"/>
/// at once: past that the reader narrows it rather than scrolling it. Every
/// state is an address (<see cref="Href"/>), so the chips are plain links and
/// the search a GET form.
/// </summary>
public sealed record DirectoryFilter(string? Body, string? County, string? Query)
{
    /// <summary>The most one list shows.</summary>
    public const int Shown = 12;

    /// <summary>One government: its key in the address, its chip, the level it holds.</summary>
    public sealed record Choice(string Key, string Label, OfficialLevel Level);

    /// <summary>The governments, in the order a reader meets them.</summary>
    public static readonly IReadOnlyList<Choice> Bodies =
    [
        new("congress", "Congress", OfficialLevel.Federal),
        new("statewide", "Statewide", OfficialLevel.StateExecutive),
        new("state-senate", "State Senate", OfficialLevel.StateSenate),
        new("state-house", "State House", OfficialLevel.StateHouse),
        new("city", "City Hall", OfficialLevel.City),
        new("county", "County", OfficialLevel.County),
    ];

    public static readonly DirectoryFilter None = new(null, null, null);

    /// <summary>The filter an address asks for. Anything unknown is ignored rather than refused.</summary>
    public static DirectoryFilter From(string? body, string? county, string? query)
    {
        var key = Bodies.FirstOrDefault(c => c.Key.Equals(body?.Trim(), StringComparison.OrdinalIgnoreCase))?.Key;
        var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (q is { Length: > 60 }) q = q[..60];
        return new DirectoryFilter(key, DistrictMatch.NormaliseCounty(county), q);
    }

    public bool IsEmpty => Body is null && County is null && Query is null;

    public bool Matches(Official official) =>
        (Body is null || Bodies.First(c => c.Key == Body).Level == official.Level)
        && (County is null || Covers(official, County))
        && (Query is null || Terms(Query).All(term => Mentions(official, term)));

    public string Href
    {
        get
        {
            var parts = new List<string>();
            if (Body is not null) parts.Add($"body={Body}");
            if (County is not null) parts.Add($"county={Uri.EscapeDataString(County)}");
            if (Query is not null) parts.Add($"q={Uri.EscapeDataString(Query)}");
            return ActPaths.Everyone + (parts.Count == 0 ? "" : "?" + string.Join('&', parts));
        }
    }

    // The senators and the statewide officers answer to every county.
    private static bool Covers(Official o, string county) =>
        o.IsSenator || o.Level == OfficialLevel.StateExecutive
        || o.Counties.Contains(county, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Terms(string query) =>
        query.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Fold).Where(t => t.Length > 0);

    // A bare number is a district: "182" is House district 182, not every
    // office with a 182 in it. Anything else is part of a name or an office,
    // without accents or punctuation, so "orourke" finds O'Rourke and "pa3"
    // finds PA-3.
    private static bool Mentions(Official o, string term) =>
        term.All(char.IsAsciiDigit)
            ? o.District == DistrictMatch.Normalise(term)
            : Fold(o.Name).Contains(term, StringComparison.Ordinal) || Fold(o.Office).Contains(term, StringComparison.Ordinal);

    // Decomposed, an accent is a mark of its own and not a letter, so it goes
    // with the spaces and the punctuation.
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
        }
        return folded.ToString();
    }
}
