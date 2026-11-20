namespace Website.Content;

/// <summary>Which government an official belongs to, which decides which district number they answer to.</summary>
public enum OfficialLevel
{
    /// <summary>US Senate and House.</summary>
    Federal,
    /// <summary>Governor, Lieutenant Governor, Attorney General: the whole state.</summary>
    StateExecutive,
    StateSenate,
    StateHouse,
    /// <summary>The City of Philadelphia: Mayor, District Attorney, City Council.</summary>
    City,
    /// <summary>The four collar counties' commissioners or councils.</summary>
    County,
}

/// <summary>Whether they hold the seat now, have been elected to it, or are leaving it.</summary>
public enum OfficialStatus
{
    Serving,
    MemberElect,
    Departing,
}

public sealed record OfficialPhone(string Label, string Number)
{
    /// <summary>The number as a <c>tel:</c> link: digits only, US country code.</summary>
    public string TelHref
    {
        get
        {
            var digits = new string(Number.Where(char.IsDigit).ToArray());
            return digits.Length == 10 ? $"tel:+1{digits}" : $"tel:{digits}";
        }
    }
}

/// <summary>
/// One thing they did or said that bears on AI: a vote, a bill, a statement, a
/// hearing. <see cref="Verified"/> is a person having checked it against
/// <see cref="Source"/>; unchecked items are shown in development only.
/// </summary>
public sealed record OfficialRecordItem(DateOnly? Date, string Summary, Uri? Source, bool Verified);

/// <summary>
/// One elected official, from a Markdown file in <c>Content/_officials/</c>:
/// the frontmatter is the facts, the body is the bio. Written by hand (or
/// drafted from research and then checked by hand), because a wrong phone
/// number on an advocacy page costs a reader's one attempt to call.
/// </summary>
public sealed record Official
{
    /// <summary>The file name, and the address: <c>/act/officials/{key}</c>.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>"U.S. Representative, PA-3", "Philadelphia City Council, District 5".</summary>
    public required string Office { get; init; }

    public required OfficialLevel Level { get; init; }

    /// <summary>
    /// The district number as the Census Bureau writes it, without leading
    /// zeros: "3" for PA-3, "182" for House district 182. Null for an office
    /// elected statewide, citywide or countywide.
    /// </summary>
    public string? District { get; init; }

    /// <summary>For a federal seat, whether it is the Senate (statewide) or the House (a district).</summary>
    public bool IsSenator { get; init; }

    public string? Party { get; init; }

    public OfficialStatus Status { get; init; } = OfficialStatus.Serving;

    public DateOnly? TermEnds { get; init; }

    /// <summary>Counties the seat covers, by name without "County": "Philadelphia", "Bucks".</summary>
    public IReadOnlyList<string> Counties { get; init; } = [];

    public IReadOnlyList<OfficialPhone> Phones { get; init; } = [];

    /// <summary>Only an address the office publishes itself. Most members of Congress publish none and use a form.</summary>
    public string? Email { get; init; }

    public Uri? ContactForm { get; init; }

    public Uri? Website { get; init; }

    /// <summary>A map of the district, elsewhere: nothing is embedded, so nothing loads from a third party.</summary>
    public Uri? Map { get; init; }

    public IReadOnlyList<string> Committees { get; init; } = [];

    public IReadOnlyList<OfficialRecordItem> AiRecord { get; init; } = [];

    /// <summary>A portrait under <c>wwwroot/img/officials/</c>, or null for the initials disc.</summary>
    public string? Photo { get; init; }

    public string? PhotoCredit { get; init; }

    /// <summary>The bio, rendered from the file's body.</summary>
    public required string BioHtml { get; init; }

    public IReadOnlyList<Uri> Sources { get; init; } = [];

    /// <summary>
    /// A person has checked this entry's contact details against the office's
    /// own site. Unchecked entries carry a "needs checking" note in development.
    /// </summary>
    public bool Verified { get; init; }

    /// <summary>When the entry was last checked or drafted.</summary>
    public DateOnly? Updated { get; init; }

    /// <summary>First and last name's initials, for the disc shown until there is a portrait.</summary>
    public string Initials
    {
        get
        {
            // "Brendan F. Boyle" is BB, "Kenyatta Johnson Jr." is KJ.
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => char.IsLetter(word[0]) && !word.EndsWith('.') && word is not ("II" or "III" or "IV"))
                .ToArray();
            return words.Length switch
            {
                0 => "?",
                1 => words[0][..1].ToUpperInvariant(),
                _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0]))
            };
        }
    }

    /// <summary>"Washington", "Harrisburg" or "Local": where the decision they take part in is made.</summary>
    public string Arena => Level switch
    {
        OfficialLevel.Federal => "Washington",
        OfficialLevel.StateExecutive or OfficialLevel.StateSenate or OfficialLevel.StateHouse => "Harrisburg",
        _ => "Local"
    };

    public string PartyInitial => Party switch
    {
        null or "" => "",
        var p when p.StartsWith("Dem", StringComparison.OrdinalIgnoreCase) => "D",
        var p when p.StartsWith("Rep", StringComparison.OrdinalIgnoreCase) => "R",
        var p when p.StartsWith("Ind", StringComparison.OrdinalIgnoreCase) => "I",
        var p => p[..1].ToUpperInvariant()
    };

    /// <summary>The first phone listed, which the file puts first because it is the one to call.</summary>
    public OfficialPhone? MainPhone => Phones.Count > 0 ? Phones[0] : null;

    public bool CanEmail => Email is not null || ContactForm is not null;
}
