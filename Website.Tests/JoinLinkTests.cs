using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// <c>/join</c> is the address printed on posters: the front page, with the
/// Join us dialog already open. It is the same page, so it names <c>/</c> as
/// canonical, and the plain front page opens nothing.
/// </summary>
public partial class JoinLinkTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string JoinPath = "/join";

    [Theory]
    [InlineData(JoinPath)]
    [InlineData("/JOIN")]
    [InlineData("/join?utm_source=poster")]
    public async Task The_poster_address_opens_the_join_dialog(string path)
    {
        var page = await factory.CreateClient().GetStringAsync(path);

        CheckedRadio(page).Should().Be("join-open");
        page.Should().Contain("rel=\"canonical\"");
    }

    [Fact]
    public async Task The_front_page_opens_no_dialog()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        CheckedRadio(page).Should().Be("dlg-none");
        page.Should().NotContain("rel=\"canonical\"");
    }

    private static string CheckedRadio(string page)
    {
        var radios = Radio().Matches(page).Where(m => m.Value.Contains("checked")).ToList();
        radios.Should().ContainSingle("exactly one dialog state is chosen on load");
        return radios[0].Groups[1].Value;
    }

    [GeneratedRegex("<input type=\"radio\" name=\"dlg\" id=\"([a-z-]+)\"[^>]*>")]
    private static partial Regex Radio();
}
