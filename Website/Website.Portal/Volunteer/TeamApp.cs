using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Website.Portal.Volunteer;

/// <summary>
/// The organizers' app: the team pages that run interactive on the server,
/// inside TeamAppLayout, on Syncfusion components. Every other page on the site
/// stays static. Pages join the list as they are rebuilt; until a team page is
/// on it, it renders static as before, and the app reaches it with a full page
/// load rather than its own router.
/// </summary>
public static class TeamApp
{
    /// <summary>Exact paths, without a trailing slash, compared ignoring case.</summary>
    private static readonly HashSet<string> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ManyHands.Core.ManyHandsPaths.Team,
        ManyHands.Core.ManyHandsPaths.TeamMembers
    };

    /// <summary>The render mode for a request: interactive for the app's pages, static (null) for the rest.</summary>
    public static IComponentRenderMode? RenderModeFor(HttpContext? http) =>
        http is not null && IsAppPage(http.Request.Path) ? RenderMode.InteractiveServer : null;

    public static bool IsAppPage(string? path) =>
        path is not null && Pages.Contains(path.Length > 1 ? path.TrimEnd('/') : path);
}
