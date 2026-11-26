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

    /// <summary>
    /// Finding your officials by address: a member's second step, and where
    /// everyone else starts once they have chosen not to sign in.
    /// </summary>
    public const string Find = "/act/find";

    /// <summary>A set of districts' officials, before its query string (<see cref="Yours(DistrictMatch)"/>).</summary>
    public const string YoursBase = "/act/yours";

    /// <summary>One message to several officials, before its query string (<see cref="WriteTo"/>).</summary>
    public const string WriteMany = "/act/write";

    /// <summary>One message to all of these, a copy each (<see cref="MailMerge"/>): ?to=key&amp;to=key.</summary>
    public static string WriteTo(IEnumerable<Official> officials) =>
        WriteMany + "?" + string.Join('&', officials.Select(o => "to=" + Uri.EscapeDataString(o.Key)));

    /// <summary>Your six for a match, as the address lookup redirects to them (<see cref="YourSix"/>).</summary>
    /// <remarks>
    /// The districts go in the query string (?cd=3&amp;ss=1&amp;sh=182&amp;county=Philadelphia),
    /// never the address, so a result can be sent to a neighbour without
    /// saying where anyone lives.
    /// </remarks>
    public static string Yours(DistrictMatch match) => Yours(match, null);

    /// <summary>The same, with a Philadelphian's council district (<c>cc=</c>), which the geocoder cannot tell us.</summary>
    public static string Yours(DistrictMatch match, string? council) =>
        YourSix.QueryFor(match, council) is { Length: > 0 } query ? YoursBase + "?" + query : YoursBase;
}
