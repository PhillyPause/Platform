using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// The Watch dialog's promise is that opening it, even by itself on a first
/// visit, tells YouTube nothing: the player arrives only when the reader
/// presses play. These pin the pieces that keep that promise, and the two
/// places that tell the reader about it.
/// </summary>
public class WatchTests
{
    private const string PrivacyEnhancedHost = "youtube-nocookie.com/embed/";

    private static readonly string Home = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "Components", "Pages", "Home.razor"));

    private static readonly string Script = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "wwwroot", "watch.js"));

    private static readonly string Privacy = File.ReadAllText(Path.Combine(
        TestPaths.ContentRoot, "privacy.md"));

    [Fact]
    public void The_page_ships_no_player() =>
        Home.Should().NotContain("<iframe", "the player is created on press, not prerendered");

    [Fact]
    public void The_player_comes_from_the_privacy_enhanced_host()
    {
        Script.Should().Contain(PrivacyEnhancedHost);
        Script.Should().NotContain("www.youtube.com/embed/");
    }

    [Fact]
    public void Closing_the_dialog_removes_the_player() =>
        Script.Should().Contain(".remove()", "a hidden iframe keeps playing its sound");

    [Fact]
    public void The_player_starts_without_captions() =>
        Script.Should().Contain("cc_load_policy=0", "captions are the reader's choice, not the default");

    [Fact]
    public void The_poster_works_without_the_script() =>
        Home.Should().MatchRegex(
            @"class=""watchposter"" href=""https://www\.youtube\.com/watch\?v=[^""]+"" target=""_blank"" rel=""noopener""");

    [Fact]
    public void The_small_print_and_the_privacy_page_both_name_youtube()
    {
        Home[Home.IndexOf("d-notices", StringComparison.Ordinal)..].Should().Contain("YouTube");
        Privacy.Should().Contain("YouTube");
    }
}
