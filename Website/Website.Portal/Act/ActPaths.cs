using Website.Content;

namespace Website.Portal.Act;

/// <summary>
/// The action centre's addresses. Each one is the front page with the Act
/// dialog open on a view of its own, so any of them can be bookmarked, shared
/// or reloaded and comes back the same.
/// </summary>
public static class ActPaths
{
    public const string Start = "/act";
    public const string Directory = "/act/officials";
    public const string Learn = "/act/learn";

    /// <summary>A set of districts' officials, before its query string (<see cref="Yours(DistrictMatch)"/>).</summary>
    public const string YoursBase = "/act/yours";

    /// <summary>One message to several officials, before its query string (<see cref="WriteTo"/>).</summary>
    public const string WriteMany = "/act/write";

    /// <summary>One message to all of these, a copy each (<see cref="MailMerge"/>): ?to=key&amp;to=key.</summary>
    public static string WriteTo(IEnumerable<Official> officials) =>
        WriteMany + "?" + string.Join('&', officials.Select(o => "to=" + Uri.EscapeDataString(o.Key)));

    /// <summary>The officials for a match, as the address lookup redirects to them.</summary>
    /// <remarks>
    /// The districts go in the query string (?cd=3&amp;ss=1&amp;sh=182&amp;county=Philadelphia),
    /// never the address, so a result can be sent to a neighbour without
    /// saying where anyone lives.
    /// </remarks>
    public static string Yours(DistrictMatch match) => Yours(match, null, null);

    /// <summary>
    /// The same, showing one of the three places (<c>in=harrisburg</c>; Washington
    /// when unset) and, for a Philadelphian, their council district (<c>cc=</c>).
    /// </summary>
    public static string Yours(DistrictMatch match, string? arena, string? council)
    {
        var parts = new List<string>();
        if (match.Congressional is { } cd) parts.Add($"cd={cd}");
        if (match.StateSenate is { } ss) parts.Add($"ss={ss}");
        if (match.StateHouse is { } sh) parts.Add($"sh={sh}");
        if (match.County is { } county) parts.Add($"county={Uri.EscapeDataString(county)}");
        if (arena is not null) parts.Add($"in={Uri.EscapeDataString(arena)}");
        if (council is not null) parts.Add($"cc={Uri.EscapeDataString(council)}");
        return YoursBase + (parts.Count == 0 ? "" : "?" + string.Join('&', parts));
    }
}
