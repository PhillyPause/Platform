using FluentAssertions;
using Website.Content;

namespace Website.Tests;

public class ContentTests
{
    private static readonly ContentStore Store = new(TestPaths.ContentRoot);

    [Fact]
    public void Every_live_markdown_file_parses()
    {
        // ContentStore throws on a missing title or description, so simply
        // constructing it is the assertion. This test exists to name that.
        Store.All.Should().NotBeEmpty();
    }

    [Fact]
    public void Privacy_resolves() => Store.Find("privacy").Should().NotBeNull();

    [Fact]
    public void An_unknown_slug_resolves_to_nothing() =>
        Store.Find("no-such-page").Should().BeNull();

    /// <summary>
    /// The long first-draft pages are set aside under <c>_archive</c> while the
    /// site is rebuilt around the explainer. They must not be reachable — a
    /// route that still answers is a page nobody is maintaining.
    /// </summary>
    [Theory]
    [InlineData("risk")]
    [InlineData("risk/now")]
    [InlineData("ask")]
    [InlineData("act")]
    [InlineData("about")]
    [InlineData("learn")]
    [InlineData("press")]
    [InlineData("pennsylvania")]
    [InlineData("_archive/about")]
    public void Set_aside_pages_are_not_routable(string slug) =>
        Store.Find(slug).Should().BeNull($"/{slug} is in _archive");

    /// <summary>
    /// Set aside is not abandoned: the archived drafts still have to parse, or
    /// they quietly rot and cannot be brought back.
    /// </summary>
    [Fact]
    public void Set_aside_pages_still_parse()
    {
        var archive = Path.Combine(TestPaths.ContentRoot, "_archive");
        if (!Directory.Exists(archive)) return;

        var store = new ContentStore(archive);
        store.All.Should().NotBeEmpty();
    }

    /// <summary>
    /// Whenever a page cites an upstream source, CC BY 4.0 needs a working link
    /// to it. A relative or malformed source renders a broken credit, which is
    /// worse than none because it looks discharged. Holds for however many
    /// adapted pages are live, including none.
    /// </summary>
    [Fact]
    public void Any_adapted_page_cites_an_absolute_https_source()
    {
        foreach (var page in Store.AdaptedPages)
        {
            page.Source!.IsAbsoluteUri.Should().BeTrue($"{page.Slug} must cite a full URL");
            page.Source.Scheme.Should().Be(Uri.UriSchemeHttps, $"{page.Slug}");
            page.SourceName.Should().NotBeNullOrWhiteSpace($"{page.Slug} must name a creator");
            page.License.Should().BeOneOf("CC-BY-4.0", "CC-BY-SA-4.0");
        }
    }

    /// <summary>
    /// Every Markdown page renders through ContentRoute, so the credit being
    /// wired in there is what makes attribution structural rather than
    /// editorial (PLAN.md section 3). Deleting it would silently drop the
    /// credit from every adapted page at once.
    /// </summary>
    [Fact]
    public void ContentRoute_renders_the_source_credit()
    {
        var route = Path.Combine(
            TestPaths.RepoRoot, "Website.Portal", "Components", "Pages", "ContentRoute.razor");

        File.ReadAllText(route).Should().Contain(
            "<SourceCredit",
            "attribution is rendered from frontmatter, not written per page");
    }

    [Fact]
    public void Descriptions_are_long_enough_to_unfurl_usefully()
    {
        // A one-word description renders a useless link preview. 60 characters
        // is roughly where a card reads as a sentence.
        foreach (var page in Store.All)
            page.Description.Length.Should().BeGreaterThan(60, $"{page.Slug}");
    }
}
