using System.Net;
using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;
using Microsoft.Extensions.DependencyInjection;
using Website.Content;
using Website.Portal.Act;

namespace ManyHands.Tests;

/// <summary>
/// ACT with ManyHands on: its first screen is join or sign in, members are
/// walked along the path (Learn, Find, Your six with Start here, then the
/// next of theirs after each contact), and anyone who chooses neither gets
/// the basic route, where nothing is saved.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ActPathTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string Districts = "cd=3&ss=1&sh=182&county=Philadelphia&cc=5";

    private PortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private YourSix Six() => YourSix.For(
        _portal.Services.GetRequiredService<OfficialsStore>(),
        new DistrictMatch("3", "1", "182", "Philadelphia"), "5", DateOnly.FromDateTime(DateTime.UtcNow));

    private async Task<HttpClient> MemberAsync()
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, "Path Walker");
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, volunteer.Id);
        return browser;
    }

    // ---------------------------------------------------------------- first screen

    [Fact]
    public async Task ACT_first_asks_to_join_or_sign_in_and_offers_a_way_without_either()
    {
        var page = await _portal.Browser().GetStringAsync(ActPaths.Start);

        page.Should().Contain("name=\"_handler\" value=\"[act]join\"", "joining is ACT's own form")
            .And.Contain("action=\"/act#act-join\"")
            .And.Contain($"href=\"{ManyHandsPaths.SignIn}?ReturnUrl=%2Fact\"", "signing in comes back to ACT")
            .And.Contain($"<a href=\"{ActPaths.Find}\">Just show me who to contact</a>")
            .And.Contain("act-path", "the path is shown before anyone chooses");
        page.Should().NotContain("class=\"act-steps\"", "nobody is on the path yet");
    }

    [Fact]
    public async Task Joining_on_ACT_answers_in_ACT()
    {
        var browser = _portal.Browser();
        var email = TestData.NewEmail();

        var response = await Forms.SubmitAsync(browser, ActPaths.Start, "[act]join",
            ("Input.DisplayName", "Act Joiner"), ("Input.Email", email));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().EndWith("/act?joined=true");
        var page = await browser.GetStringAsync("/act?joined=true");
        page.Should().Contain("Check your email").And.Contain($"href=\"{ActPaths.Find}\"");

        var volunteer = await new VolunteerRepository(db.DataSource).GetByEmailAsync(email);
        volunteer.Should().NotBeNull();
        volunteer!.EmailVerifiedAt.Should().BeNull("joining still waits for the email to be confirmed");
    }

    [Fact]
    public async Task The_join_dialogs_form_still_answers_on_the_front_page()
    {
        var response = await Forms.SubmitAsync(_portal.Browser(), ManyHandsPaths.Join, "join",
            ("Input.DisplayName", "Front Joiner"), ("Input.Email", TestData.NewEmail()));

        response.Headers.Location!.ToString().Should().EndWith(ManyHandsPaths.CheckEmail, "the front page holds both forms, each answering its own");
    }

    // ---------------------------------------------------------------- the basic route

    [Fact]
    public async Task Without_signing_in_the_basic_route_shows_all_of_them_and_invites_joining()
    {
        var browser = _portal.Browser();

        var find = await browser.GetStringAsync(ActPaths.Find);
        var yours = await browser.GetStringAsync($"{ActPaths.YoursBase}?{Districts}");

        find.Should().Contain("Find your officials").And.Contain("Join or sign in").And.NotContain("class=\"act-steps\"");
        yours.Should().Contain("How to do it, in three lines").And.Contain("Join or sign in")
            .And.NotContain("act-start-here").And.NotContain("class=\"act-steps\"");
        foreach (var official in Six().People) yours.Should().Contain(official.Name);
    }

    // ---------------------------------------------------------------- members

    [Fact]
    public async Task A_member_starts_at_Learn_and_goes_on_to_Find()
    {
        var browser = await MemberAsync();

        var learn = await browser.GetStringAsync(ActPaths.Start);
        var find = await browser.GetStringAsync(ActPaths.Find);

        learn.Should().Contain("Two minutes before you start");
        WebUtility.HtmlDecode(learn).Should().Contain($"<a class=\"act-btn\" href=\"{ActPaths.Find}\">I’m ready</a>");
        learn.Should().Contain("<li class=\"act-step-now\" aria-current=\"step\">").And.NotContain("[act]join");
        find.Should().Contain("Where do you live?").And.Contain("act-step-done").And.NotContain("Join or sign in");
    }

    [Fact]
    public async Task A_members_six_marks_Start_here_and_every_link_carries_the_districts()
    {
        var browser = await MemberAsync();
        var six = Six();

        var page = await browser.GetStringAsync($"{ActPaths.YoursBase}?{Districts}");

        page.Should().Contain("Your six").And.Contain("act-start-here").And.Contain("act-card-start");
        page.IndexOf(six.StartHere!.Name, StringComparison.Ordinal).Should().BeLessThan(
            page.IndexOf(six.People[1].Name, StringComparison.Ordinal), "Start here comes first");
        page.Should().Contain($"href=\"/act/call/{six.People[1].Key}?{Districts.Replace("&", "&amp;")}\"");
        page.Should().NotContain("How to do it, in three lines", "a member is walked through it instead");
    }

    [Fact]
    public async Task After_a_call_a_member_is_offered_the_next_of_theirs_and_after_the_last_a_friend()
    {
        var browser = await MemberAsync();
        var six = Six();
        var first = six.People.First(o => o.Phones.Count > 0 && six.After(o) is not null);
        var last = six.People.Last(o => o.Phones.Count > 0);

        var next = await Forms.SubmitAsync(browser, $"/act/call/{first.Key}?{Districts}", "log",
            ("Input.Outcome", nameof(OutreachOutcome.SpokeToSomeone)));
        var end = await Forms.SubmitAsync(browser, $"/act/call/{last.Key}?{Districts}", "log",
            ("Input.Outcome", nameof(OutreachOutcome.LeftVoicemail)));

        var nextPage = await next.Content.ReadAsStringAsync();
        nextPage.Should().Contain("That counts").And.Contain("Next of yours").And.Contain(six.After(first)!.Name);
        var endPage = await end.Content.ReadAsStringAsync();
        if (six.After(last) is null)
        {
            endPage.Should().Contain("That is everyone on your list").And.Contain("href=\"mailto:?subject=Five%20minutes%20on%20AI");
        }
    }

    [Fact]
    public async Task A_call_reached_from_the_directory_has_no_list_to_go_on_with()
    {
        var browser = await MemberAsync();
        var first = Six().People.First(o => o.Phones.Count > 0);

        var response = await Forms.SubmitAsync(browser, $"/act/call/{first.Key}", "log",
            ("Input.Outcome", nameof(OutreachOutcome.NoAnswer)));

        var page = await response.Content.ReadAsStringAsync();
        page.Should().Contain("Contact someone else").And.NotContain("Next of yours").And.NotContain("class=\"act-steps\"");
    }
}
