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
    [InlineData("/volunteer/me")]
    [InlineData("/volunteer/sign-in")]
    [InlineData("/volunteer/calendar.ics")]
    [InlineData("/team")]
    public async Task ManyHands_paths_are_not_found_while_it_is_off(string path)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_home_page_needs_no_database()
    {
        var response = await factory.CreateClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
