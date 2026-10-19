using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManyHands.Tests;

/// <summary>
/// The site with ManyHands on: the front page's event box comes from the
/// in-memory copy, the join buttons lead to ManyHands, privacy says what it
/// holds — and every one of them falls back to the static page when there is
/// nothing to show. The flag-off half is in Website.Tests.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteWiringTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string StaticEventDay = "Wednesday 28 October";

    private PortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private static ActivityListItem Activity(string title, DateTimeOffset startsAt, string? description = null) => new(
        Guid.NewGuid(), title, description, ActivityKind.OneTime, "Clark Park", startsAt, startsAt.AddHours(2),
        MeetingUrl: null, SignupCount: 0, Capacity: null, Tags: []);

    [Fact]
    public async Task The_event_box_shows_the_next_ManyHands_activity_with_a_way_to_sign_up()
    {
        var next = Activity("Clark Park leafleting", DateTimeOffset.UtcNow.AddDays(2), "Bring a friend.");
        _portal.Services.GetRequiredService<UpcomingActivities>()
            .Replace([Activity("Later thing", DateTimeOffset.UtcNow.AddDays(9)), next]);

        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain("Clark Park leafleting").And.Contain("Bring a friend.")
            .And.Contain($"href=\"{ManyHandsPaths.Activity(next.Id)}\"")
            .And.Contain($"href=\"{ManyHandsPaths.Calendar}\"")
            .And.NotContain(StaticEventDay).And.NotContain("Later thing");
    }

    [Fact]
    public async Task With_nothing_coming_up_the_event_box_is_the_static_one()
    {
        _portal.Services.GetRequiredService<UpcomingActivities>().Replace([]);

        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain(StaticEventDay).And.NotContain($"href=\"{ManyHandsPaths.Calendar}\"");
    }

    [Fact]
    public async Task The_join_buttons_lead_to_ManyHands()
    {
        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain($"class=\"signupbtn\" href=\"{ManyHandsPaths.Join}\"")
            .And.Contain($"class=\"signupbtn\" href=\"{ManyHandsPaths.Activities}\"")
            .And.Contain("phillypause.session");
    }

    [Fact]
    public async Task Privacy_says_what_ManyHands_holds()
    {
        var privacy = await _portal.Browser().GetStringAsync("/privacy");

        privacy.Should().Contain("If you sign up on this site").And.Contain(ManyHandsPaths.Me);
    }

    [Fact]
    public async Task Signed_out_the_front_page_offers_sign_in_at_the_top()
    {
        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain("class=\"account-bar\"")
            .And.Contain($"class=\"account-primary\" href=\"{ManyHandsPaths.SignIn}\"")
            .And.NotContain("Signed in as");
    }

    [Fact]
    public async Task Signed_in_every_page_says_who_and_is_never_cached()
    {
        var member = await TestData.VolunteerAsync(db.DataSource, "Maya Member");
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, member.Id);

        foreach (var path in new[] { "/", "/privacy", ManyHandsPaths.Activities })
        {
            var response = await browser.GetAsync(path);
            var page = await response.Content.ReadAsStringAsync();

            page.Should().Contain("Signed in as <strong>Maya Member</strong>", path)
                .And.Contain($"href=\"{ManyHandsPaths.Me}\"")
                .And.NotContain($"class=\"account-primary\" href=\"{ManyHandsPaths.Team}\"");
            response.Headers.CacheControl!.NoStore.Should().BeTrue(path);
        }
    }

    [Fact]
    public async Task An_organizer_gets_the_team_button_on_the_front_page()
    {
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Olu Organizer", VolunteerRole.Organizer);
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, organizer.Id);

        var home = await browser.GetStringAsync("/");

        home.Should().Contain("Signed in as <strong>Olu Organizer</strong>")
            .And.Contain($"class=\"account-primary\" href=\"{ManyHandsPaths.Team}\"");
    }

    [Fact]
    public async Task Signing_out_from_the_front_page_signs_out()
    {
        var member = await TestData.VolunteerAsync(db.DataSource, "Sam Signout");
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, member.Id);

        await Forms.SubmitAsync(browser, "/", "account-sign-out");

        (await browser.GetStringAsync("/")).Should().NotContain("Signed in as");
        (await browser.GetAsync(ManyHandsPaths.Me)).StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public void A_finished_activity_drops_out_of_the_box_even_if_the_cache_is_stale()
    {
        var clock = new StepClock(DateTimeOffset.UtcNow);
        var cache = new UpcomingActivities(clock);
        cache.Replace([Activity("Tonight", clock.GetUtcNow().AddHours(1))]);

        clock.Now += TimeSpan.FromHours(4);

        cache.Next.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_good_copy()
    {
        var cache = new UpcomingActivities(TimeProvider.System);
        cache.Replace([Activity("Kept", DateTimeOffset.UtcNow.AddDays(1))]);
        await using var unreachable = Npgsql.NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1");
        var worker = new UpcomingActivitiesWorker(
            new ActivityRepository(unreachable), cache, NullLogger<UpcomingActivitiesWorker>.Instance);

        await worker.RefreshAsync(CancellationToken.None);

        cache.Next!.Title.Should().Be("Kept");
    }

    private sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
