using System.Globalization;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Website.Content;

/// <summary>
/// The districts an address falls in, as the Census Bureau's geocoder names
/// them, or as a reader picked them by hand. Numbers without leading zeros.
/// </summary>
public sealed record DistrictMatch(
    string? Congressional,
    string? StateSenate,
    string? StateHouse,
    string? County,
    bool InPennsylvania = true)
{
    public bool IsEmpty => Congressional is null && StateSenate is null && StateHouse is null && County is null;

    /// <summary>"03" and "3" are the same district.</summary>
    public static string? Normalise(string? number) =>
        string.IsNullOrWhiteSpace(number) ? null
        : int.TryParse(number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n.ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary>"Philadelphia County" and "philadelphia" are the same county.</summary>
    public static string? NormaliseCounty(string? county)
    {
        if (string.IsNullOrWhiteSpace(county)) return null;
        var name = county.Trim();
        if (name.EndsWith(" County", StringComparison.OrdinalIgnoreCase)) name = name[..^" County".Length];
        return OfficialsStore.Counties.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Every official in <c>Content/_officials/*.md</c>, read and rendered once at
/// startup like the rest of the content (ContentStore). The folder is set
/// aside from ContentStore by its underscore, so a file here is never a page
/// of its own: the act pages decide how an official is shown.
///
/// A malformed file stops the site starting, as a malformed page does: a
/// directory that silently drops someone is worse than one that will not boot.
/// </summary>
public sealed class OfficialsStore
{
    public const string Folder = "_officials";

    /// <summary>Greater Philadelphia, as this directory covers it: the five southeastern counties.</summary>
    public static readonly IReadOnlyList<string> Counties = ["Philadelphia", "Bucks", "Chester", "Delaware", "Montgomery"];

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAutoLinks()
        .UseSmartyPants()
        .Build();

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly Dictionary<string, Official> _byKey;

    public OfficialsStore(string contentRoot)
    {
        var folder = Path.Combine(contentRoot, Folder);
        _byKey = !Directory.Exists(folder)
            ? new(StringComparer.OrdinalIgnoreCase)
            : Directory.EnumerateFiles(folder, "*.md")
                .Select(Parse)
                .ToDictionary(official => official.Key, StringComparer.OrdinalIgnoreCase);

        All = _byKey.Values.OrderBy(o => o, Ordering.Instance).ToArray();
    }

    /// <summary>Everyone, in the order a reader meets them: Washington, Harrisburg, then local.</summary>
    public IReadOnlyList<Official> All { get; }

    public Official? Find(string? key) => key is null ? null : _byKey.GetValueOrDefault(key.Trim());

    /// <summary>Everyone whose seat is still held or about to be on <paramref name="today"/>.</summary>
    public IEnumerable<Official> Current(DateOnly today) =>
        All.Where(o => o.TermEnds is null || o.TermEnds >= today);

    /// <summary>
    /// The officials who represent someone in these districts. Philadelphia's
    /// district council members all come back, because the Census geocoder
    /// does not know council districts: the page says which is theirs to find.
    /// </summary>
    public IReadOnlyList<Official> For(DistrictMatch match, DateOnly today) =>
        Current(today).Where(o => Represents(o, match)).ToArray();

    public IEnumerable<Official> InCounty(string county, DateOnly today) =>
        Current(today).Where(o => o.Counties.Contains(county, StringComparer.OrdinalIgnoreCase)
                                  || (o.Level is OfficialLevel.StateExecutive || o.IsSenator));

    private static bool Represents(Official o, DistrictMatch match) => o.Level switch
    {
        OfficialLevel.Federal when o.IsSenator => match.InPennsylvania,
        OfficialLevel.Federal => match.Congressional is not null && o.District == match.Congressional,
        OfficialLevel.StateExecutive => match.InPennsylvania,
        OfficialLevel.StateSenate => match.StateSenate is not null && o.District == match.StateSenate,
        OfficialLevel.StateHouse => match.StateHouse is not null && o.District == match.StateHouse,
        OfficialLevel.City or OfficialLevel.County =>
            match.County is not null && o.Counties.Contains(match.County, StringComparer.OrdinalIgnoreCase),
        _ => false
    };

    private static Official Parse(string path)
    {
        var key = Path.GetFileNameWithoutExtension(path);
        var name = $"{Folder}/{Path.GetFileName(path)}";
        var document = Markdig.Markdown.Parse(File.ReadAllText(path), Pipeline);

        var block = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault()
            ?? throw new InvalidDataException($"{name} has no frontmatter.");
        var yaml = string.Join('\n', block.Lines.Lines.Select(l => l.ToString()).Where(l => l is not "---"));

        FrontMatter meta;
        try
        {
            meta = Yaml.Deserialize<FrontMatter>(yaml) ?? throw new InvalidDataException($"{name} has empty frontmatter.");
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new InvalidDataException($"{name}: {e.Message}", e);
        }

        if (string.IsNullOrWhiteSpace(meta.Name)) throw new InvalidDataException($"{name} has no name.");
        if (string.IsNullOrWhiteSpace(meta.Office)) throw new InvalidDataException($"{name} has no office.");

        var level = ParseLevel(meta.Level, name);
        var district = DistrictMatch.Normalise(meta.District);
        if (level is OfficialLevel.StateSenate or OfficialLevel.StateHouse && district is null)
            throw new InvalidDataException($"{name} sits in the General Assembly but has no district.");
        if (level is OfficialLevel.Federal && !meta.Senator && district is null)
            throw new InvalidDataException($"{name} is in the House but has no district.");

        var counties = (meta.Counties ?? []).Select(c => DistrictMatch.NormaliseCounty(c)
            ?? throw new InvalidDataException($"{name} names '{c}', which is not one of the five counties.")).ToArray();

        // Links in a bio open a new window, as every external link on the site does.
        foreach (var link in document.Descendants<Markdig.Syntax.Inlines.LinkInline>())
        {
            if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var attributes = Markdig.Renderers.Html.HtmlAttributesExtensions.GetAttributes(link);
                attributes.AddPropertyIfNotExist("target", "_blank");
                attributes.AddPropertyIfNotExist("rel", "noopener");
            }
        }

        return new Official
        {
            Key = key,
            Name = meta.Name.Trim(),
            Office = meta.Office.Trim(),
            Level = level,
            District = district,
            IsSenator = meta.Senator,
            Party = meta.Party,
            Status = ParseStatus(meta.Status, name),
            TermEnds = meta.TermEnds,
            TermStarts = meta.TermStarts,
            Offices = (meta.Offices ?? [])
                .Where(o => !string.IsNullOrWhiteSpace(o.Address))
                .Select(o => new OfficialOffice(o.Label ?? "Office", o.Address!.Trim()))
                .ToArray(),
            Counties = counties,
            Phones = (meta.Phones ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Number))
                .Select(p => new OfficialPhone(p.Label ?? "Office", p.Number!.Trim()))
                .ToArray(),
            Email = string.IsNullOrWhiteSpace(meta.Email) ? null : meta.Email.Trim(),
            ContactForm = Link(meta.ContactForm, name),
            Website = Link(meta.Website, name),
            Map = Link(meta.Map, name),
            Committees = meta.Committees ?? [],
            AiRecord = (meta.AiRecord ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Summary))
                .Select(item => new OfficialRecordItem(item.Date, item.Summary!.Trim(), Link(item.Source, name), item.Verified))
                .OrderByDescending(item => item.Date)
                .ToArray(),
            Photo = string.IsNullOrWhiteSpace(meta.Photo) ? null : meta.Photo.Trim(),
            PhotoCredit = meta.PhotoCredit,
            BioHtml = document.ToHtml(Pipeline).Trim(),
            Sources = (meta.Sources ?? []).Select(s => Link(s, name)).OfType<Uri>().ToArray(),
            Verified = meta.Verified,
            Updated = meta.Updated
        };
    }

    private static Uri? Link(string? value, string file)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidDataException($"{file}: '{value}' is not a full https:// address.");
    }

    private static OfficialLevel ParseLevel(string? value, string file) => value?.Trim().ToLowerInvariant() switch
    {
        "federal" => OfficialLevel.Federal,
        "state-executive" => OfficialLevel.StateExecutive,
        "state-senate" => OfficialLevel.StateSenate,
        "state-house" => OfficialLevel.StateHouse,
        "city" => OfficialLevel.City,
        "county" => OfficialLevel.County,
        _ => throw new InvalidDataException(
            $"{file}: level '{value}' is not one of federal, state-executive, state-senate, state-house, city, county.")
    };

    private static OfficialStatus ParseStatus(string? value, string file) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "serving" => OfficialStatus.Serving,
        "member-elect" => OfficialStatus.MemberElect,
        "departing" => OfficialStatus.Departing,
        _ => throw new InvalidDataException($"{file}: status '{value}' is not serving, member-elect or departing.")
    };

    /// <summary>Washington, then Harrisburg, then local; senators before representatives; then by district.</summary>
    private sealed class Ordering : IComparer<Official>
    {
        public static readonly Ordering Instance = new();

        public int Compare(Official? x, Official? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var byLevel = x.Level.CompareTo(y.Level);
            if (byLevel != 0) return byLevel;

            var bySenate = y.IsSenator.CompareTo(x.IsSenator);
            if (bySenate != 0) return bySenate;

            var byDistrict = (x.District is null).CompareTo(y.District is null);
            if (byDistrict != 0) return byDistrict;
            if (x.District is not null && y.District is not null)
            {
                var byNumber = int.Parse(x.District, CultureInfo.InvariantCulture)
                    .CompareTo(int.Parse(y.District, CultureInfo.InvariantCulture));
                if (byNumber != 0) return byNumber;
            }

            var byStatus = x.Status.CompareTo(y.Status);
            if (byStatus != 0) return byStatus;

            var bySurname = string.Compare(x.SortName, y.SortName, StringComparison.OrdinalIgnoreCase);
            return bySurname != 0 ? bySurname : string.CompareOrdinal(x.Name, y.Name);
        }
    }

    /// <summary>Shape of the YAML block. Keys are underscored: <c>contact_form</c>, <c>ai_record</c>.</summary>
    private sealed class FrontMatter
    {
        public string? Name { get; set; }
        public string? Office { get; set; }
        public string? Level { get; set; }
        public string? District { get; set; }
        public bool Senator { get; set; }
        public string? Party { get; set; }
        public string? Status { get; set; }
        public DateOnly? TermEnds { get; set; }
        public DateOnly? TermStarts { get; set; }
        public List<OfficeYaml>? Offices { get; set; }
        public List<string>? Counties { get; set; }
        public List<PhoneYaml>? Phones { get; set; }
        public string? Email { get; set; }
        public string? ContactForm { get; set; }
        public string? Website { get; set; }
        public string? Map { get; set; }
        public List<string>? Committees { get; set; }
        public List<RecordYaml>? AiRecord { get; set; }
        public string? Photo { get; set; }
        public string? PhotoCredit { get; set; }
        public List<string>? Sources { get; set; }
        public bool Verified { get; set; }
        public DateOnly? Updated { get; set; }
    }

    private sealed class OfficeYaml
    {
        public string? Label { get; set; }
        public string? Address { get; set; }
    }

    private sealed class PhoneYaml
    {
        public string? Label { get; set; }
        public string? Number { get; set; }
    }

    private sealed class RecordYaml
    {
        public DateOnly? Date { get; set; }
        public string? Summary { get; set; }
        public string? Source { get; set; }
        public bool Verified { get; set; }
    }
}
