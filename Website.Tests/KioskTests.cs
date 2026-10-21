using System.Text.Json;
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

    [Fact]
    public void Motion_stops_for_readers_who_ask() =>
        Styles.Should().Contain("@media (prefers-reduced-motion: reduce)");

    [Fact]
    public void The_site_never_links_to_the_kiosk() =>
        Home.Should().NotContain(KioskPath);
}
