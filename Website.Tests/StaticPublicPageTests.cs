using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// App.razor may make a page interactive, but only the organizers' app. A
/// public page that came out interactive would open a server connection for
/// every reader and load Syncfusion for nothing.
/// </summary>
public class StaticPublicPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The comment Blazor leaves where a server-interactive component was prerendered.</summary>
    private const string ServerComponentMarker = "\"type\":\"server\"";

    [Theory]
    [InlineData("/")]
    [InlineData("/join")]
    [InlineData("/privacy")]
    [InlineData("/license")]
    [InlineData("/kiosk")]
    public async Task A_public_page_is_static_and_loads_no_Syncfusion(string path)
    {
        var page = await factory.CreateClient().GetStringAsync(path);

        // The import map names every module the site could load, Syncfusion's too;
        // naming one loads nothing. What must be absent is the script and the theme.
        page.Should().NotContain(ServerComponentMarker)
            .And.NotContain("<script src=\"_content/Syncfusion").And.NotContain("href=\"_content/Syncfusion.Blazor.Themes");
    }
}
