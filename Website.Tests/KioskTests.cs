using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// The kiosk (/kiosk) is the table's screen: installable on its own, kept
/// out of search, and never linked from the site a reader sees.
/// </summary>
public class KioskTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string KioskPath = "/kiosk";
    private const string ManifestPath = "/kiosk.webmanifest";
    private const string WorkerPath = "/kiosk-sw.js";

    private static readonly string Script = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "wwwroot", "kiosk.js"));

    private static readonly string Styles = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "wwwroot", "kiosk.css"));

    private static readonly string Home = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "Components", "Pages", "Home.razor"));

    [Fact]
    public async Task The_kiosk_is_unindexed_and_names_its_manifest()
    {
        var page = await factory.CreateClient().GetStringAsync(KioskPath);

        page.Should().Contain("<meta name=\"robots\" content=\"noindex\"")
            .And.Contain($"<link rel=\"manifest\" href=\"{ManifestPath}\"")
            .And.Contain("data-kiosk");
    }

    [Fact]
    public async Task The_manifest_installs_the_kiosk_and_nothing_else()
    {
        var client = factory.CreateClient();

        var reply = await client.GetAsync(ManifestPath);
        var manifest = JsonDocument.Parse(await reply.Content.ReadAsStringAsync()).RootElement;

        reply.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json");
        manifest.GetProperty("start_url").GetString().Should().Be(KioskPath);
        manifest.GetProperty("scope").GetString().Should().Be(KioskPath);
        manifest.GetProperty("display").GetString().Should().Be("fullscreen");
        foreach (var icon in manifest.GetProperty("icons").EnumerateArray())
        {
            var iconReply = await client.GetAsync(icon.GetProperty("src").GetString());
            iconReply.IsSuccessStatusCode.Should().BeTrue(icon.GetProperty("src").GetString());
        }
    }

    [Fact]
    public async Task The_worker_is_served_and_registered_for_the_kiosk_alone()
    {
        var reply = await factory.CreateClient().GetAsync(WorkerPath);

        reply.IsSuccessStatusCode.Should().BeTrue();
        Script.Should().Contain($"register('{WorkerPath}', {{ scope: '{KioskPath}' }})");
    }

    /// <summary>
    /// 13vmin is 1.7-inch type on the StanbyME 2's 27-inch panel, capitals
    /// over the 1⅛ inches ADA signage asks for at ten feet. The framed site
    /// and its overlay are read close up, in pixels, and are not held to it.
    /// </summary>
    [Fact]
    public void Type_on_the_loop_reads_from_ten_feet()
    {
        const double SmallestVmin = 13;

        var sizes = Regex.Matches(Styles, @"font(?:-size)?:\s*([^;}]+)")
            .SelectMany(declaration => Regex.Matches(declaration.Groups[1].Value, @"([\d.]+)vmin"))
            .Select(size => double.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        sizes.Should().NotBeEmpty().And.OnlyContain(size => size >= SmallestVmin);
        Script.Should().Contain($"const FLOOR = {(SmallestVmin / 100).ToString(CultureInfo.InvariantCulture)};");
    }

    [Fact]
    public void Motion_stops_for_readers_who_ask() =>
        Styles.Should().Contain("@media (prefers-reduced-motion: reduce)");

    [Fact]
    public void The_site_never_links_to_the_kiosk() =>
        Home.Should().NotContain(KioskPath);
}
