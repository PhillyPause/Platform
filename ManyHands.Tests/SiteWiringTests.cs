using FluentAssertions;
using ManyHands.Core;

namespace ManyHands.Tests;

/// <summary>
/// The site with ManyHands on: the account bar, the join buttons leading to
/// ManyHands, privacy saying what it holds, and the front page's Next event
/// left as written. The flag-off half is in Website.Tests.
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

    /// <summary>
    /// The front page's Next event is hand-edited and stays so: a published
    /// ManyHands activity, sooner or not, never takes its place.
    /// </summary>
    [Fact]
    public async Task The_next_event_is_the_hand_written_one_whatever_ManyHands_has_published()
    {
        await TestData.ActivityAsync(db.DataSource, "Tomorrow's leafleting", startsInHours: 20);

        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain(StaticEventDay).And.Contain("The AI Doc")
            .And.NotContain("Tomorrow's leafleting");
    }

    [Fact]
    public async Task The_join_dialog_holds_the_signup_form_and_I_can_help_leads_to_ManyHands()
    {
        var home = await _portal.Browser().GetStringAsync("/");

        home.Should().Contain($"action=\"{ManyHandsPaths.Join}#join-form\"")
            .And.Contain("name=\"_handler\" value=\"join\"")
            .And.Contain($"class=\"signupbtn\" href=\"{ManyHandsPaths.Activities}\"")
            .And.Contain("phillypause.session");
    }

    /// <summary>What we keep opens over Join us, with the privacy page's own text, ManyHands' part included.</summary>
    [Fact]
    public async Task What_we_keep_is_a_dialog_over_join_us_with_the_privacy_page_in_it()
    {
        var home = await _portal.Browser().GetStringAsync(ManyHandsPaths.Join);

        home.Should().Contain("<input type=\"checkbox\" id=\"privacy-open\"")
            .And.Contain("<label class=\"joinnote-link\" for=\"privacy-open\">What we keep</label>")
            .And.Contain("aria-labelledby=\"privacydlg-title\"")
            .And.Contain("If you sign up on this site");
    }

    [Fact]
    public async Task Joining_in_the_dialog_saves_the_volunteer_and_answers_in_the_dialog()
    {
        var email = TestData.NewEmail();

        var response = await Forms.SubmitAsync(_portal.Browser(), ManyHandsPaths.Join, "join",
            ("Input.DisplayName", "Dialog Joiner"), ("Input.Email", email),
            ("interest", TestData.Flyering.ToString()));

        var page = await response.Content.ReadAsStringAsync();
        page.Should().Contain("id=\"join-open\" class=\"dlgtoggle\" checked")
            .And.Contain("A welcome email is on its way");
        var volunteers = new ManyHands.Data.VolunteerRepository(db.DataSource);
        var joined = await volunteers.GetByEmailAsync(email);
        joined!.DisplayName.Should().Be("Dialog Joiner");
        (await volunteers.GetInterestIdsAsync(joined.Id)).Should().Equal(TestData.Flyering);
    }

    /// <summary>The browser's required checks are what gray the Join button; the server holds the same line.</summary>
    [Fact]
    public async Task Joining_needs_a_name_as_well_as_an_email()
    {
        var email = TestData.NewEmail();
        var home = await _portal.Browser().GetStringAsync(ManyHandsPaths.Join);

        var response = await Forms.SubmitAsync(_portal.Browser(), ManyHandsPaths.Join, "join",
            ("Input.DisplayName", "   "), ("Input.Email", email));

        home.Should().Contain(@"autocomplete=""name"" required pattern="".*\S.*""")
            .And.Contain("autocomplete=\"email\" required");
        (await response.Content.ReadAsStringAsync()).Should().Contain("We need a name to greet you by");
        (await new ManyHands.Data.VolunteerRepository(db.DataSource).GetByEmailAsync(email)).Should().BeNull();
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

}
