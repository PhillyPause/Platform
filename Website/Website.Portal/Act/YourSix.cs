using Website.Content;

namespace Website.Portal.Act;

/// <summary>
/// A reader's own officials, in the order the path takes them: their US
/// Representative, both Senators, their State Senator and State
/// Representative, then whoever is theirs at home. At home that is a
/// Philadelphian's district councilmember, once they have chosen their
/// council district, or the county's commissioners or council anywhere else.
/// Six for a Philadelphian, a few more in the counties. The statewide and
/// citywide officers are everyone's, so they are reached through the
/// directory rather than listed here.
/// </summary>
/// <remarks>
/// The first is Start here: the US Representative, because Congress is where
/// a pause would be decided and a House office counts its own district's calls.
/// Without a House district it is the first one known.
/// </remarks>
public sealed class YourSix
{
    private YourSix(DistrictMatch match, string? council, IReadOnlyList<Official> people)
    {
        Match = match;
        Council = council;
        People = people;
    }

    public DistrictMatch Match { get; }

    /// <summary>A Philadelphian's council district, which the geocoder cannot tell us; null until they choose it.</summary>
    public string? Council { get; }

    /// <summary>In the path's order, Start here first.</summary>
    public IReadOnlyList<Official> People { get; }

    public Official? StartHere => People.Count > 0 ? People[0] : null;

    public bool IsPhiladelphia => Match.County == "Philadelphia";

    /// <summary>A Philadelphian who has not yet said which council district is theirs.</summary>
    public bool NeedsCouncil => IsPhiladelphia && Council is null;

    public static YourSix For(OfficialsStore store, DistrictMatch match, string? council, DateOnly today)
    {
        var philadelphia = match.County == "Philadelphia";
        var districts = CouncilDistricts(store, today);
        var chosen = philadelphia && council is not null && districts.Contains(council) ? council : null;

        // Someone elected but not yet sworn in has no office to call: the one
        // who holds the seat today is the one to reach.
        var found = match.IsEmpty
            ? []
            : store.For(match, today).Where(o => o.Status != OfficialStatus.MemberElect).ToArray();

        var people = new List<Official>();
        people.AddRange(found.Where(o => o.Level == OfficialLevel.Federal && !o.IsSenator));
        people.AddRange(found.Where(o => o.Level == OfficialLevel.Federal && o.IsSenator).OrderBy(o => o.Name, StringComparer.Ordinal));
        people.AddRange(found.Where(o => o.Level == OfficialLevel.StateSenate));
        people.AddRange(found.Where(o => o.Level == OfficialLevel.StateHouse));
        people.AddRange(philadelphia
            ? found.Where(o => o.Level == OfficialLevel.City && o.District is not null && o.District == chosen)
            : found.Where(o => o.Level == OfficialLevel.County).OrderBy(o => o.Name, StringComparer.Ordinal));

        return new YourSix(match, chosen, people);
    }

    /// <summary>Philadelphia's council districts, as chips to choose from.</summary>
    public static IReadOnlyList<string> CouncilDistricts(OfficialsStore store, DateOnly today) =>
        store.Current(today)
            .Where(o => o.Level == OfficialLevel.City && o.District is not null)
            .Select(o => o.District!)
            .Distinct()
            .OrderBy(d => int.Parse(d, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    /// <summary>Who comes after this one on the path; null for the last, or for someone who is not one of these.</summary>
    public Official? After(Official official)
    {
        for (var i = 0; i < People.Count - 1; i++)
        {
            if (People[i].Key == official.Key) return People[i + 1];
        }
        return null;
    }

    public bool Includes(Official official) => People.Any(o => o.Key == official.Key);

    /// <summary>
    /// The districts as a query string, without its "?": carried from Your
    /// six to a call or write page and back, so the page can say who is next.
    /// Never the address.
    /// </summary>
    public string Query => QueryFor(Match, Council);

    /// <summary>Your six again, as a link.</summary>
    public string Back => ActPaths.Yours(Match, Council);

    public string CallPath(Official official) => $"/act/call/{official.Key}{Suffix}";

    public string WritePath(Official official) => $"/act/write/{official.Key}{Suffix}";

    private string Suffix => Query is { Length: > 0 } query ? "?" + query : "";

    internal static string QueryFor(DistrictMatch match, string? council)
    {
        var parts = new List<string>();
        if (match.Congressional is { } cd) parts.Add($"cd={cd}");
        if (match.StateSenate is { } ss) parts.Add($"ss={ss}");
        if (match.StateHouse is { } sh) parts.Add($"sh={sh}");
        if (match.County is { } county) parts.Add($"county={Uri.EscapeDataString(county)}");
        if (council is not null) parts.Add($"cc={Uri.EscapeDataString(council)}");
        return string.Join('&', parts);
    }

    /// <summary>
    /// The six a call or write page was reached from, when its address
    /// carries districts and this official is one of them; null otherwise,
    /// as from the directory.
    /// </summary>
    public static YourSix? Carried(
        OfficialsStore store, Official official, string? cd, string? ss, string? sh, string? county, string? cc, DateOnly today)
    {
        var match = Read(cd, ss, sh, county);
        if (match.IsEmpty) return null;
        var six = For(store, match, DistrictMatch.Normalise(cc), today);
        return six.Includes(official) ? six : null;
    }

    /// <summary>
    /// Reads the districts back from a query string's values, as Your six,
    /// the call page and the write page all receive them.
    /// </summary>
    public static DistrictMatch Read(string? cd, string? ss, string? sh, string? county) =>
        new(DistrictMatch.Normalise(cd), DistrictMatch.Normalise(ss), DistrictMatch.Normalise(sh), DistrictMatch.NormaliseCounty(county));
}
