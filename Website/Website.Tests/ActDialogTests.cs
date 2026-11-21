using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Website.Content;
using Website.Portal.Act;

namespace Website.Tests;

/// <summary>
/// The action centre is a dialog on the front page, not a section of its own:
/// ACT at the end of the nav band and the call to action under the headline open it,
/// every /act address is the front page with it open on a view of its own,
/// and every inner page carries the way in.
/// </summary>
public partial class ActDialogTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly OfficialsStore Store = new(TestPaths.ContentRoot);

    [Fact]
    public async Task The_front_page_offers_Act_in_the_nav_and_under_the_headline_and_opens_nothing()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        CheckedRadio(page).Should().Be("dlg-none");
        NavItem().Matches(Nav().Match(page).Value).Select(m => m.Groups[1].Value.Trim()).TakeLast(2)
            .Should().Equal(["Join", "Act"], "the nav band ends JOIN, then ACT in the corner");
        page.Should().Contain("<label class=\"actbtn\" for=\"act-open\">")
            .And.Contain("<label class=\"actcall\" for=\"act-open\">")
            .And.Contain("Tell the people who represent you", "the dialog's first view waits behind the button")
            .And.Contain("class=\"paneldlg d-act\"", "opened by the button, it fades in like the others");
    }

    public static TheoryData<string> Addresses()
    {
        var official = Store.All.First(o => o.Phones.Count > 0 && o.CanEmail);
        return new TheoryData<string>
        {
            "/act", "/act/yours?cd=3&ss=1&sh=182&county=Philadelphia", "/act/officials", "/act/learn", "/act/learn/calling",
            $"/act/officials/{official.Key}", $"/act/call/{official.Key}", $"/act/write/{official.Key}"
        };
    }

    [Theory]
    [MemberData(nameof(Addresses))]
    public async Task Every_act_address_is_the_front_page_with_the_Act_dialog_open(string path)
    {
        var page = await factory.CreateClient().GetStringAsync(path);

        CheckedRadio(page).Should().Be("act-open");
        page.Should().Contain("class=\"masthead\"", "the front page is behind the dialog")
            .And.Contain("class=\"paneldlg d-act d-act-loaded\"", "a step between views must not fade the dialog in again");
    }

    [Fact]
    public async Task The_dialogs_three_places_are_your_officials_Greater_Philly_and_how_it_works()
    {
        var page = await factory.CreateClient().GetStringAsync("/act");

        TabLabel().Matches(Tabs().Match(page).Value).Select(m => m.Groups[1].Value)
            .Should().Equal(["Your officials", "Greater Philly", "How it works"]);
    }

    [Fact]
    public async Task An_official_names_the_page_and_the_front_page_does_not()
    {
        var official = Store.All.First();

        var page = await factory.CreateClient().GetStringAsync($"/act/officials/{official.Key}");

        Title().Match(page).Groups[1].Value.Should().StartWith(official.Name);
        page.Should().NotContain("<title>Stop the AI death race");
    }

    [Fact]
    public async Task Moving_between_the_dialogs_views_loads_each_address_in_full()
    {
        var page = await factory.CreateClient().GetStringAsync("/act");

        // Enhanced navigation would patch the radio's checked attribute, which
        // a radio the reader has clicked ignores, and the dialog would shut.
        page.Should().Contain("aria-labelledby=\"actdlg-title\" data-enhance-nav=\"false\"");
    }

    [Fact]
    public async Task Every_inner_page_carries_Act_now_in_its_header()
    {
        var page = await factory.CreateClient().GetStringAsync("/privacy");

        page.Should().Contain("class=\"header-act\" href=\"/act\" data-enhance-nav=\"false\"");
    }

    [Fact]
    public async Task An_address_typed_on_the_front_page_goes_to_act_and_lands_on_the_districts_never_the_address()
    {
        var match = new DistrictMatch("3", "1", "182", "Philadelphia", InPennsylvania: true);
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDistrictLookup>(new StubLookup(match))));
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var front = await client.GetStringAsync("/");
        front.Should().Contain("action=\"/act\"", "a problem with the address must come back with the dialog open");

        var response = await client.PostAsync("/act", new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", Token().Match(front).Groups[1].Value),
            new("_handler", "find"),
            new("Input.Address", "1400 John F Kennedy Blvd, Philadelphia, PA 19107")
        ]));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().EndWith(ActPaths.Yours(match))
            .And.NotContain("Kennedy");
    }

    public static TheoryData<string, string?, bool> Elsewhere() => new()
    {
        { "Pittsburgh: Pennsylvania, outside the five counties", "17", true },
        { "Camden: across the river, outside Pennsylvania", null, false }
    };

    [Theory]
    [MemberData(nameof(Elsewhere))]
    public async Task An_address_outside_the_five_counties_is_sent_to_ControlAI_not_shown_a_list(string where, string? congressional, bool inPennsylvania)
    {
        // The geocoder's answer, as Parse reads it: a county outside the five is no county.
        var match = new DistrictMatch(congressional, null, null, null, inPennsylvania);
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDistrictLookup>(new StubLookup(match))));
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var front = await client.GetStringAsync("/");

        var response = await client.PostAsync("/act", new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", Token().Match(front).Groups[1].Value),
            new("_handler", "find"),
            new("Input.Address", where)
        ]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} stays on the first view rather than landing on a list", where);
        var page = await response.Content.ReadAsStringAsync();
        CheckedRadio(page).Should().Be("act-open");
        page.Should().Contain("That address is outside Greater Philadelphia")
            .And.Contain($"href=\"{ControlAi}\" target=\"_blank\" rel=\"noopener\">Act with ControlAI</a>");
    }

    [Fact]
    public async Task Anyone_outside_the_five_counties_is_pointed_at_ControlAI_before_typing_an_address()
    {
        var page = await factory.CreateClient().GetStringAsync("/");

        page.Should().Contain("Outside Philadelphia and the four counties around it?")
            .And.Contain($"href=\"{ControlAi}\" target=\"_blank\" rel=\"noopener\"");
    }

    private const string ControlAi = "https://act.controlai.org/";

    private static string CheckedRadio(string page)
    {
        var radios = Radio().Matches(page).Where(m => m.Value.Contains("checked")).ToList();
        radios.Should().ContainSingle("exactly one dialog state is chosen on load");
        return radios[0].Groups[1].Value;
    }

    private sealed class StubLookup(DistrictMatch match) : IDistrictLookup
    {
        public bool IsAvailable => true;

        public Task<DistrictMatch?> LookupAsync(string address, CancellationToken ct = default) =>
            Task.FromResult<DistrictMatch?>(match);
    }

    [GeneratedRegex("<input type=\"radio\" name=\"dlg\" id=\"([a-z-]+)\"[^>]*>")]
    private static partial Regex Radio();

    [GeneratedRegex(@"<nav class=""navband""[\s\S]*?</nav>")]
    private static partial Regex Nav();

    [GeneratedRegex(@"<label class=""[a-z ]+"" for=""[a-z-]+"">(?:<svg[\s\S]*?</svg>)?([^<]*)</label>")]
    private static partial Regex NavItem();

    [GeneratedRegex(@"<nav class=""act-tabs""[\s\S]*?</nav>")]
    private static partial Regex Tabs();

    [GeneratedRegex(@"<a [^>]*>([^<]*)</a>")]
    private static partial Regex TabLabel();

    [GeneratedRegex("<title>([^<]*)</title>")]
    private static partial Regex Title();

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Token();
}
