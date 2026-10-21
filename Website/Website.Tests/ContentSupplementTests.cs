using System.Text.RegularExpressions;
using FluentAssertions;
using Website.Content;

namespace Website.Tests;

/// <summary>
/// What a page says about ManyHands is a supplement, shown only while it is
/// on; and every external link in content opens a new window.
/// </summary>
public partial class ContentSupplementTests
{
    private const string PrivacySlug = "privacy";
    private const string VolunteerHeading = "If you sign up on this site";

    private static readonly ContentStore Store = new(TestPaths.ContentRoot);

    private static ContentPage Privacy => Store.Find(PrivacySlug)!;

    [Fact]
    public void Privacy_says_nothing_about_volunteering_while_ManyHands_is_off() =>
        Store.HtmlFor(Privacy, manyHandsOn: false).Should().NotContain(VolunteerHeading);

    [Fact]
    public void Privacy_describes_what_ManyHands_holds_where_its_marker_is_when_on()
    {
        var html = Store.HtmlFor(Privacy, manyHandsOn: true);

        html.Should().Contain(VolunteerHeading).And.NotContain(ContentStore.ManyHandsMarker);
        html.IndexOf(VolunteerHeading, StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("Unsubscribing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_supplement_is_never_a_page_of_its_own() =>
        Store.Find($"{ContentStore.ManyHandsFolder}/{PrivacySlug}").Should().BeNull();

    [Fact]
    public void Every_external_link_in_content_opens_a_new_window()
    {
        foreach (var page in Store.All)
        {
            foreach (var html in new[] { Store.HtmlFor(page, false), Store.HtmlFor(page, true) })
            {
                foreach (Match link in ExternalLink().Matches(html))
                    link.Value.Should().Contain("target=\"_blank\"").And.Contain("rel=\"noopener\"", $"{page.Slug}: {link.Value}");
            }
        }
    }

    [GeneratedRegex("<a [^>]*href=\"https?://[^>]*>")]
    private static partial Regex ExternalLink();
}
