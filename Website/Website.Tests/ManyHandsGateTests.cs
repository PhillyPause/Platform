using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Website.Tests;

/// <summary>
/// With ManyHands off — the default, and production today — its pages must be
/// plain 404s, not errors from services that were never registered, and the
/// site must still serve. Driven through the real pipeline.
/// </summary>
public class ManyHandsGateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/volunteer")]
    [InlineData("/events")]
    [InlineData("/volunteer/me")]
    [InlineData("/volunteer/sign-in")]
    [InlineData("/volunteer/calendar.ics")]
    [InlineData("/volunteer/confirm/abc")]
    [InlineData("/volunteer/welcome")]
    [InlineData("/volunteer/check-email")]
    [InlineData("/volunteer/remove/00000000-0000-0000-0000-000000000001")]
    [InlineData("/team")]
    public async Task ManyHands_paths_are_not_found_while_it_is_off(string path)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/privacy")]
    public async Task While_it_is_off_no_page_links_to_it_or_mentions_it(string path)
    {
        var page = await factory.CreateClient().GetStringAsync(path);

        page.Should().NotContain("/volunteer").And.NotContain("events-open").And.NotContain("phillypause.session")
            .And.NotContain("account-bar")
            .And.NotContain("If you sign up on this site");
    }

    [Fact]
    public async Task While_it_is_off_there_is_no_email_signup_and_I_can_help_opens_Join_us()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        page.Should().NotContain("mailto:join@")
            .And.Contain("<label class=\"signupbtn\" for=\"join-open\">I can help</label>");
    }

    [Fact]
    public async Task The_home_page_needs_no_database()
    {
        var response = await factory.CreateClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
