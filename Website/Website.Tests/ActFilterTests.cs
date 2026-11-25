using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Website.Content;
using Website.Portal.Act;

namespace Website.Tests;

/// <summary>
/// Nobody reads a list of dozens, so the action centre never shows one: the
/// directory is a filter that lists nobody until something is chosen and no
/// more than a dozen at once, and "Your officials" shows one place at a
/// time. Chips say how many they hold; every state is an address.
/// </summary>
public partial class ActFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly OfficialsStore Store = new(TestPaths.ContentRoot);

    private static IReadOnlyList<Official> Current => Store.Current(DateOnly.FromDateTime(DateTime.UtcNow)).ToArray();

    // ---------------------------------------------------------------- the directory

    [Fact]
    public async Task The_directory_lists_nobody_until_something_is_chosen()
    {
        var page = await factory.CreateClient().GetStringAsync(ActPaths.Directory);

        Cards(page).Should().Be(0);
        WebUtility.HtmlDecode(page).Should().Contain("<h2>Greater Philadelphia’s officials</h2>", "the heading names the area, not \"everyone\"")
            .And.Contain("Choose a government or a county")
            .And.Contain("class=\"act-chip\" href=\"/act/officials?body=congress\"");
    }

    [Theory]
    [InlineData("Bucks", "Bucks County’s officials")]
    [InlineData("Philadelphia", "Philadelphia’s officials")]
    public async Task A_county_names_its_own_officials(string county, string heading)
    {
        var page = await factory.CreateClient().GetStringAsync($"/act/officials?county={county}");

        WebUtility.HtmlDecode(page).Should().Contain($"<h2>{heading}</h2>").And.Contain($"<title>{heading}");
    }

    [Fact]
    public async Task A_choice_with_more_than_a_dozen_shows_a_dozen_and_asks_for_more_to_narrow_it()
    {
        var house = Current.Count(o => o.Level == OfficialLevel.StateHouse);
        house.Should().BeGreaterThan(DirectoryFilter.Shown, "the test needs a list longer than a page");

        var page = await factory.CreateClient().GetStringAsync("/act/officials?body=state-house");

        Cards(page).Should().Be(DirectoryFilter.Shown);
        page.Should().Contain($"{house} people match, so here are the first {DirectoryFilter.Shown}")
            .And.Contain("Choose a county, or search for a name or a district number");
    }

    [Fact]
    public async Task The_chosen_chip_clears_itself_and_the_others_keep_it()
    {
        var page = await factory.CreateClient().GetStringAsync("/act/officials?body=state-house&county=Bucks");

        page.Should().Contain("href=\"/act/officials?county=Bucks\" aria-current=\"true\"", "pressing State House again drops it")
            .And.Contain("href=\"/act/officials?body=state-house&amp;county=Chester\"", "another county keeps State House");
    }

    [Fact]
    public async Task A_chip_that_would_show_nobody_is_not_a_link()
    {
        // Philadelphia's government is the city's: no county commissioners.
        var page = await factory.CreateClient().GetStringAsync("/act/officials?county=Philadelphia");

        page.Should().Contain("<span class=\"act-chip is-empty\">County<span class=\"act-chip-n\">0</span></span>");
    }

    [Fact]
    public async Task A_search_that_the_chips_hide_offers_the_search_without_them()
    {
        var krasner = Current.First(o => o.Office.StartsWith("District Attorney", StringComparison.Ordinal));
        var surname = krasner.SortName;

        var page = await factory.CreateClient().GetStringAsync($"/act/officials?body=congress&q={surname}");

        Cards(page).Should().Be(0);
        page.Should().Contain($"href=\"/act/officials?q={surname}\">Search all {Current.Count} for");
    }

    [Fact]
    public void A_number_is_a_district_and_a_name_is_found_without_its_punctuation()
    {
        var byDistrict = Current.Where(DirectoryFilter.From("state-house", null, "182").Matches).ToArray();
        byDistrict.Should().ContainSingle().Which.District.Should().Be("182");

        Current.Where(DirectoryFilter.From(null, null, "orourke").Matches)
            .Should().Contain(o => o.Name.Contains("O'Rourke", StringComparison.Ordinal) || o.Name.Contains("O’Rourke", StringComparison.Ordinal));

        Current.Where(DirectoryFilter.From(null, null, "pa-3").Matches)
            .Should().NotBeEmpty().And.OnlyContain(o => o.Level == OfficialLevel.Federal && o.District == "3");
    }

    [Fact]
    public void Anything_the_filter_does_not_know_is_ignored()
    {
        DirectoryFilter.From("galactic-senate", "Atlantis", "  ").Should().Be(DirectoryFilter.None);
    }

    // ---------------------------------------------------------------- your officials

    private const string Philadelphian = "/act/yours?cd=3&ss=1&sh=182&county=Philadelphia";

    [Fact]
    public async Task Your_officials_start_with_Washington_alone()
    {
        var washington = Current.Count(o => o.IsSenator || (o.Level == OfficialLevel.Federal && o.District == "3"));

        var page = await factory.CreateClient().GetStringAsync(Philadelphian);

        Cards(page).Should().Be(washington, "the two senators and PA-3's member, or members while the seat changes hands");
        page.Should().Contain("href=\"/act/yours?cd=3&amp;ss=1&amp;sh=182&amp;county=Philadelphia&amp;in=harrisburg\"")
            .And.Contain("county=Philadelphia&amp;in=local\"", "City Hall is a chip away");
    }

    [Fact]
    public async Task A_Philadelphian_sees_the_citywide_officials_and_only_their_own_district_member()
    {
        var citywide = Current.Count(o => o.Level == OfficialLevel.City && o.District is null);
        var member = Current.First(o => o.Level == OfficialLevel.City && o.District == "5");

        var without = await factory.CreateClient().GetStringAsync(Philadelphian + "&in=local");
        var with = await factory.CreateClient().GetStringAsync(Philadelphian + "&in=local&cc=5");

        Cards(without).Should().Be(citywide);
        without.Should().Contain("Choose yours to add your district").And.NotContain(member.Name);
        Cards(with).Should().Be(citywide + 1);
        with.Should().Contain(member.Name);
    }

    private static int Cards(string page) => Card().Matches(page).Count;

    [GeneratedRegex("<li class=\"act-card\">")]
    private static partial Regex Card();
}
