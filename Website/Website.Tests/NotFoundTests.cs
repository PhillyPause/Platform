using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// A 404 is the front page with its Not found dialog open, so a mistyped
/// address still lands on the site; and it is a real 404, never a 200 that
/// search engines would index.
/// </summary>
public class NotFoundTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DialogOpen = "id=\"notfound-open\" class=\"dlgtoggle\" checked";

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/a/b/c")]
    [InlineData("/volunteer/check-email")] // ManyHands is off here: the gate's 404
    public async Task An_unknown_address_is_a_404_on_the_front_page_with_the_dialog_open(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        var page = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        page.Should().Contain(DialogOpen)
            .And.Contain("That page does not exist")
            .And.Contain("class=\"masthead\"")
            .And.Contain("<meta name=\"robots\" content=\"noindex\">");
        page.Should().NotContain("id=\"dlg-none\" class=\"dlgtoggle\" checked");
    }

    [Fact]
    public async Task The_front_page_itself_has_no_Not_found_dialog()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        page.Should().NotContain("That page does not exist").And.NotContain(DialogOpen);
    }
}
