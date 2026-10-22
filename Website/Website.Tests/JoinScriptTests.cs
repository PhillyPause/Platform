using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// Join asks the site whether it is there before the form goes, so a bad
/// connection (a phone hotspot at a table) never trades what someone typed
/// for the browser's error page.
/// </summary>
public class JoinScriptTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Script = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website", "Website.Portal", "wwwroot", "join.js"));

    [Fact]
    public async Task Every_page_loads_the_join_check()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        page.Should().MatchRegex(@"<script type=""module"" src=""join(\.[a-z0-9]+)?\.js""");
    }

    [Fact]
    public void The_check_asks_for_our_manifest_and_holds_the_form_when_it_does_not_come()
    {
        Script.Should().Contain("/kiosk.webmanifest?probe=")
            .And.Contain("includes('manifest')")
            .And.Contain("event.preventDefault();")
            .And.Contain("What you typed is still here");
    }
}
