using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Website.Content;

/// <summary>
/// Reads every <c>*.md</c> under a content root once, renders it, and holds the
/// result. Registered as a singleton, so a request never touches the disk or
/// the Markdown parser.
/// </summary>
/// <remarks>
/// PLAN.md section 2.3 calls for compiling content at build time via an MSBuild
/// task. This loads at startup instead — same single parse, same zero
/// per-request cost, a fraction of the machinery. Moving it into the build is a
/// later optimisation that changes no caller.
/// </remarks>
public sealed class ContentStore
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAutoLinks()
        .UsePipeTables()
        .UseFootnotes()
        .UseSmartyPants()
        .Build();

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly Dictionary<string, ContentPage> _bySlug;

    public ContentStore(string contentRoot)
    {
        if (!Directory.Exists(contentRoot))
            throw new DirectoryNotFoundException($"Content root not found: {contentRoot}");

        _bySlug = Directory
            .EnumerateFiles(contentRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsSetAside(path, contentRoot))
            .Select(path => Parse(path, contentRoot))
            .ToDictionary(page => page.Slug, StringComparer.OrdinalIgnoreCase);

        NavPages = _bySlug.Values
            .Where(page => page.Nav is not null)
            .OrderBy(page => page.Order)
            .ThenBy(page => page.Title, StringComparer.Ordinal)
            .ToArray();

        AdaptedPages = _bySlug.Values
            .Where(page => page.RequiresAttribution)
            .OrderBy(page => page.Slug, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Pages carrying a <c>nav:</c> label, in nav order.</summary>
    public IReadOnlyList<ContentPage> NavPages { get; }

    /// <summary>Pages that owe CC BY attribution. The /license page lists these.</summary>
    public IReadOnlyList<ContentPage> AdaptedPages { get; }

    public IReadOnlyCollection<ContentPage> All => _bySlug.Values;

    public ContentPage? Find(string? slug) =>
        _bySlug.GetValueOrDefault((slug ?? string.Empty).Trim('/'));

    private static ContentPage Parse(string path, string contentRoot)
    {
        var markdown = File.ReadAllText(path);
        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        var frontMatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault()
            ?? throw new InvalidDataException(
                $"{Relative(path, contentRoot)} has no frontmatter. Every page needs at least a title and a description.");

        var yaml = string.Join('\n', frontMatter.Lines.Lines
            .Select(line => line.ToString())
            .Where(line => line is not "---"));

        var meta = Yaml.Deserialize<FrontMatter>(yaml)
            ?? throw new InvalidDataException($"{Relative(path, contentRoot)} has empty frontmatter.");

        if (string.IsNullOrWhiteSpace(meta.Title))
            throw new InvalidDataException($"{Relative(path, contentRoot)} has no title.");

        if (string.IsNullOrWhiteSpace(meta.Description))
            throw new InvalidDataException(
                $"{Relative(path, contentRoot)} has no description — it would unfurl as a bare link.");

        return new ContentPage
        {
            Slug = SlugFor(path, contentRoot),
            Title = meta.Title,
            Description = meta.Description,
            Html = Markdig.Markdown.ToHtml(markdown, Pipeline),
            Nav = meta.Nav,
            Order = meta.Order ?? int.MaxValue,
            Source = meta.Source is null ? null : new Uri(meta.Source),
            SourceName = meta.SourceName ?? "PauseAI",
            License = meta.License ?? "CC-BY-4.0",
            Updated = meta.Updated
        };
    }

    /// <summary>
    /// Any path with an underscore-prefixed segment is set aside: drafts and
    /// retired pages stay in the repo, under version control and easy to bring
    /// back, without being routable or appearing in the nav.
    /// <c>Content/_archive/risk/index.md</c> is not a page.
    /// </summary>
    private static bool IsSetAside(string path, string contentRoot) =>
        Relative(path, contentRoot)
            .Split('/')
            .Any(segment => segment.StartsWith('_'));

    /// <summary>
    /// Path on disk to route: <c>risk/overview.md</c> becomes <c>risk/overview</c>,
    /// and an <c>index.md</c> becomes its folder (so <c>risk/index.md</c> is
    /// <c>risk</c>, and the root <c>index.md</c> is the home page).
    /// </summary>
    private static string SlugFor(string path, string contentRoot)
    {
        var relative = Relative(path, contentRoot);
        var withoutExtension = relative[..^".md".Length];

        if (withoutExtension.Equals("index", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return withoutExtension.EndsWith("/index", StringComparison.OrdinalIgnoreCase)
            ? withoutExtension[..^"/index".Length]
            : withoutExtension;
    }

    private static string Relative(string path, string contentRoot) =>
        Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Shape of the YAML block. Keys are underscored: <c>source_name</c>.</summary>
    private sealed class FrontMatter
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Nav { get; set; }
        public int? Order { get; set; }
        public string? Source { get; set; }
        public string? SourceName { get; set; }
        public string? License { get; set; }
        public DateOnly? Updated { get; set; }
    }
}
