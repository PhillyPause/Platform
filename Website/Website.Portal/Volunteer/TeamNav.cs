using ManyHands.Core;

namespace Website.Portal.Volunteer;

/// <summary>
/// Every place in the organizers' sidebar, in one list. TeamShell draws it on
/// every team page, static or interactive, so a link added here shows up on
/// all of them at once; nothing else lists the team's places.
/// </summary>
public static class TeamNav
{
    /// <summary>
    /// One place. <paramref name="Path"/> is where it goes; <see cref="Owns"/>
    /// are the path prefixes of the pages beneath it, such as one activity's
    /// roster under Activities, so the sidebar still says where you are.
    /// </summary>
    public sealed record Link(string Label, string Path, string Icon)
    {
        public IReadOnlyList<string> Owns { get; init; } = [];
    }

    public sealed record Section(string Heading, IReadOnlyList<Link> Links);

    /// <summary>
    /// Places outside the team pages, last in the sidebar. A new public area
    /// that organizers should reach from here joins this list. Declared
    /// before <see cref="Sections"/>, which reads it as it is built.
    /// </summary>
    public static IReadOnlyList<Link> SiteLinks { get; } =
    [
        new("My page", ManyHandsPaths.Me, Symbols.Person),
        new("Public events", ManyHandsPaths.Events, Symbols.Public),
        new("Action centre", "/act", Symbols.Campaign),
        new("Front page", "/", Symbols.Home)
    ];

    /// <summary>The sidebar, top to bottom. Add a link to the section it belongs in.</summary>
    public static IReadOnlyList<Section> Sections { get; } =
    [
        new("Volunteering",
        [
            new("Activities", ManyHandsPaths.Team, Symbols.Event) { Owns = [$"{ManyHandsPaths.Team}/activity/"] },
            new("Members", ManyHandsPaths.TeamMembers, Symbols.Group),
            new("Invites", ManyHandsPaths.TeamInvites, Symbols.PersonAdd)
        ]),
        new("Lobbying",
        [
            new("Organizations", ManyHandsPaths.Crm, Symbols.Apartment) { Owns = [$"{ManyHandsPaths.Crm}/organization/"] },
            new("Pipeline", ManyHandsPaths.CrmPipeline, Symbols.ViewKanban) { Owns = [$"{ManyHandsPaths.Crm}/engagement/"] },
            new("Officials", ManyHandsPaths.CrmOfficials, Symbols.AccountBalance),
            new("Outreach", ManyHandsPaths.TeamOutreach, Symbols.Campaign)
        ]),
        new("Partners",
        [
            new("Partners", ManyHandsPaths.TeamPartners, Symbols.Handshake),
            new("Import", ManyHandsPaths.TeamPartnersImport, Symbols.UploadFile)
        ]),
        new("Mail",
        [
            new("Inbox", ManyHandsPaths.Inbox, Symbols.Inbox) { Owns = [$"{ManyHandsPaths.Inbox}/"] },
            new("Write", ManyHandsPaths.InboxCompose, Symbols.Edit),
            new("Templates", ManyHandsPaths.InboxTemplates, Symbols.Description)
        ]),
        new("Site", SiteLinks)
    ];

    /// <summary>
    /// The place a page belongs to: the link to exactly this path, or else the
    /// one owning the longest prefix of it. Null for a page under none.
    /// </summary>
    public static Link? Current(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var bare = path.Split('?', '#')[0];
        if (bare.Length > 1) bare = bare.TrimEnd('/');

        var links = Sections.SelectMany(s => s.Links).ToList();

        return links.FirstOrDefault(l => string.Equals(l.Path, bare, StringComparison.OrdinalIgnoreCase))
            ?? links
                .SelectMany(l => l.Owns
                    .Where(prefix => bare.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(prefix => (Link: l, prefix.Length)))
                .OrderByDescending(match => match.Length)
                .Select(match => match.Link)
                .FirstOrDefault();
    }
}
