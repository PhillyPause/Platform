namespace Website.Content;

/// <summary>
/// One Markdown file, parsed. <see cref="Html"/> is already rendered — nothing
/// downstream parses Markdown per request.
/// </summary>
public sealed record ContentPage
{
    /// <summary>Route, without a leading slash. The empty string is the home page.</summary>
    public required string Slug { get; init; }

    public required string Title { get; init; }

    /// <summary>Meta description and OG description. Required: an advocacy page without one is a wasted link unfurl.</summary>
    public required string Description { get; init; }

    public required string Html { get; init; }

    /// <summary>Label in the main nav. Null keeps the page out of it.</summary>
    public string? Nav { get; init; }

    /// <summary>Nav ordering. Unset sorts last.</summary>
    public int Order { get; init; } = int.MaxValue;

    /// <summary>Upstream page this was adapted from, or null for original work.</summary>
    public Uri? Source { get; init; }

    /// <summary>Who to credit. Defaults to PauseAI, which is the only upstream we adapt.</summary>
    public string SourceName { get; init; } = "PauseAI";

    /// <summary>SPDX identifier of the upstream licence.</summary>
    public string License { get; init; } = "CC-BY-4.0";

    public DateOnly? Updated { get; init; }

    /// <summary>
    /// True when CC BY 4.0 attribution is owed. The credit is rendered from
    /// these fields rather than written by hand, because the licence requires
    /// five separate elements and prose forgets one.
    /// </summary>
    public bool RequiresAttribution => Source is not null;
}
