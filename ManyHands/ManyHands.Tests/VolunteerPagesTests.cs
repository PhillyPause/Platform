using System.Net;
using Dapper;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Discord;
using Microsoft.Extensions.DependencyInjection;

namespace ManyHands.Tests;

/// <summary>
/// The public pages, through the real host: what a person sees, what the forms
/// do, and the two things a mail scanner must not be able to do on its own —
/// spend a sign-in link, or unsubscribe someone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VolunteerPagesTests(DatabaseFixture db) : IAsyncLifetime
{
    private PortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private async Task<string> SignInLinkAsync(Guid volunteerId) =>
        ManyHandsPaths.RedeemSignIn(await new AuthTokenRepository(db.DataSource).IssueAsync(volunteerId));

    [Fact]
    public async Task A_published_activity_is_on_the_list_and_a_draft_is_not()
    {
        var title = $"Listed {Guid.NewGuid():N}";
        var draftTitle = $"Draft {Guid.NewGuid():N}";
        await TestData.ActivityAsync(db.DataSource, title);
        await TestData.ActivityAsync(db.DataSource, draftTitle, published: false);

        var page = await _portal.Browser().GetStringAsync(ManyHandsPaths.Events);

        Assert.Contains(title, page);
        Assert.DoesNotContain(draftTitle, page);
    }

    [Fact]
    public async Task An_unknown_activity_is_a_404()
    {
        var response = await _portal.Browser().GetAsync(ManyHandsPaths.Activity(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signing_up_through_the_form_records_the_signup()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();

        var response = await Forms.SubmitAsync(_portal.Browser(), ManyHandsPaths.Activity(activity), "signup",
            ("Input.DisplayName", "Form Filler"), ("Input.Email", email),
            ("interest", TestData.Tabling.ToString()));

        Assert.Contains("You are signed up", await response.Content.ReadAsStringAsync());

        var volunteers = new VolunteerRepository(db.DataSource);
        var volunteer = await volunteers.GetByEmailAsync(email);
        Assert.NotNull(volunteer);
        var interests = await volunteers.GetInterestIdsAsync(volunteer.Id);
        Assert.Equal([TestData.Tabling], interests.ToArray());
    }

    [Fact]
    public async Task Opening_a_sign_in_link_does_not_spend_it_and_the_button_does()
    {
        var volunteerId = await new VolunteerRepository(db.DataSource).UpsertByEmailAsync(TestData.NewEmail(), "Scanned");
        var link = await SignInLinkAsync(volunteerId);
        var scanner = _portal.Browser();

        // What a mail scanner does: open the link, and nothing else.
        await scanner.GetAsync(link);
        await scanner.GetAsync(link);

        var person = _portal.Browser();
        var signedIn = await Forms.SubmitAsync(person, link, "redeem");

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(ManyHandsPaths.Me, signedIn.Headers.Location?.PathAndQuery);
        Assert.Contains("Hello, Scanned", await person.GetStringAsync(ManyHandsPaths.Me));
    }

    [Fact]
    public async Task A_spent_link_sends_the_person_back_to_ask_again()
    {
        var volunteerId = await new VolunteerRepository(db.DataSource).UpsertByEmailAsync(TestData.NewEmail(), "Twice");
        var link = await SignInLinkAsync(volunteerId);
        await Forms.SubmitAsync(_portal.Browser(), link, "redeem");

        var again = await Forms.SubmitAsync(_portal.Browser(), link, "redeem");

        Assert.Equal($"{ManyHandsPaths.SignIn}?expired=true", again.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task My_page_needs_a_sign_in()
    {
        var response = await _portal.Browser().GetAsync(ManyHandsPaths.Me);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(ManyHandsPaths.SignIn, response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Asking_for_a_sign_in_link_says_the_same_thing_for_any_address()
    {
        var response = await Forms.SubmitAsync(_portal.Browser(), ManyHandsPaths.SignIn, "sign-in",
            ("Input.Email", TestData.NewEmail()));

        Assert.Contains("Check your email", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Opening_an_unsubscribe_link_asks_and_the_button_unsubscribes()
    {
        var volunteers = new VolunteerRepository(db.DataSource);
        var volunteer = (await volunteers.GetByIdAsync(
            await volunteers.UpsertByEmailAsync(TestData.NewEmail(), "Leaver")))!;
        var link = ManyHandsPaths.Unsubscribe(volunteer.CommsKey);

        await _portal.Browser().GetAsync(link);
        Assert.True((await volunteers.GetByIdAsync(volunteer.Id))!.CanReceiveEmail);

        await Forms.SubmitAsync(_portal.Browser(), link, "unsubscribe");
        Assert.False((await volunteers.GetByIdAsync(volunteer.Id))!.CanReceiveEmail);
    }

    [Fact]
    public async Task Opening_a_confirmation_link_does_nothing_and_the_button_confirms_and_signs_in()
    {
        var volunteers = new VolunteerRepository(db.DataSource);
        var joined = Assert.IsType<JoinResult.Ok>(await volunteers.JoinAsync("Confirmer", TestData.NewEmail(), []));
        var link = ManyHandsPaths.ConfirmJoin(await new AuthTokenRepository(db.DataSource).IssueConfirmationAsync(joined.VolunteerId));

        await _portal.Browser().GetAsync(link);
        Assert.Null((await volunteers.GetByIdAsync(joined.VolunteerId))!.EmailVerifiedAt);

        var person = _portal.Browser();
        var answer = await Forms.SubmitAsync(person, link, "confirm");

        Assert.Contains("You are in, Confirmer", await answer.Content.ReadAsStringAsync());
        Assert.NotNull((await volunteers.GetByIdAsync(joined.VolunteerId))!.EmailVerifiedAt);
        Assert.Contains("Hello, Confirmer", await person.GetStringAsync(ManyHandsPaths.Me));

        var again = await Forms.SubmitAsync(_portal.Browser(), link, "confirm");
        Assert.Contains("This link has done its job", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Opening_a_removal_link_asks_and_the_button_removes()
    {
        var volunteers = new VolunteerRepository(db.DataSource);
        var joined = Assert.IsType<JoinResult.Ok>(await volunteers.JoinAsync("Mistaken", TestData.NewEmail(), []));
        var link = ManyHandsPaths.Remove((await volunteers.GetByIdAsync(joined.VolunteerId))!.CommsKey);

        await _portal.Browser().GetAsync(link);
        Assert.False((await volunteers.GetByIdAsync(joined.VolunteerId))!.IsDeleted);

        var answer = await Forms.SubmitAsync(_portal.Browser(), link, "remove");

        Assert.Contains("you are off our list", await answer.Content.ReadAsStringAsync());
        Assert.True((await volunteers.GetByIdAsync(joined.VolunteerId))!.IsDeleted);
    }

    [Fact]
    public async Task A_mail_clients_one_click_unsubscribe_needs_no_token()
    {
        var volunteers = new VolunteerRepository(db.DataSource);
        var volunteer = (await volunteers.GetByIdAsync(
            await volunteers.UpsertByEmailAsync(TestData.NewEmail(), "One Click")))!;

        var response = await _portal.Browser().PostAsync(
            ManyHandsPaths.OneClickUnsubscribe(volunteer.CommsKey),
            new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await volunteers.GetByIdAsync(volunteer.Id))!.CanReceiveEmail);
    }

    [Fact]
    public async Task The_public_calendar_carries_dated_work_and_an_unknown_personal_feed_is_a_404()
    {
        var title = $"Calendar {Guid.NewGuid():N}";
        await TestData.ActivityAsync(db.DataSource, title);
        var browser = _portal.Browser();

        var feed = await browser.GetStringAsync(ManyHandsPaths.Calendar);
        var missing = await browser.GetAsync(ManyHandsPaths.PersonalCalendar(Guid.NewGuid()));

        Assert.Contains($"SUMMARY:{title}", feed);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Deleting_yourself_anonymises_the_record_and_signs_you_out()
    {
        var email = TestData.NewEmail();
        var volunteerId = await new VolunteerRepository(db.DataSource).UpsertByEmailAsync(email, "Goodbye");
        var browser = _portal.Browser();
        await Forms.SubmitAsync(browser, await SignInLinkAsync(volunteerId), "redeem");

        await Forms.SubmitAsync(browser, ManyHandsPaths.Me, "delete", ("Delete.Confirm", "true"));

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<(string Name, string? Email, bool Deleted)>(
            "SELECT display_name, email, is_deleted FROM volunteers WHERE id = @volunteerId", new { volunteerId });
        Assert.True(row.Deleted);
        Assert.Null(row.Email);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(ManyHandsPaths.Me)).StatusCode);
    }

    [Fact]
    public async Task A_Discord_link_asks_a_stranger_to_sign_in_and_links_on_the_button_only()
    {
        var links = _portal.Services.GetRequiredService<DiscordLinks>();
        var discordId = (ulong)Random.Shared.NextInt64(1L << 50, 1L << 60);
        var path = ManyHandsPaths.LinkDiscord(links.Issue(discordId, "maya"));
        var volunteer = await TestData.VolunteerAsync(db.DataSource, "Maya Discord");
        var queries = new DiscordQueries(db.DataSource);

        var stranger = await _portal.Browser().GetStringAsync(path);
        Assert.Contains(ManyHandsPaths.SignIn, stranger);

        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, volunteer.Id);
        Assert.Contains("Link my Discord", await browser.GetStringAsync(path));
        Assert.Null(await queries.GetVolunteerByDiscordIdAsync(discordId));

        await Forms.SubmitAsync(browser, path, "link-discord");
        Assert.Equal(volunteer.Id, (await queries.GetVolunteerByDiscordIdAsync(discordId))!.Id);

        await Forms.SubmitAsync(browser, ManyHandsPaths.Me, "unlink-discord");
        Assert.Null(await queries.GetVolunteerByDiscordIdAsync(discordId));
    }

    [Fact]
    public async Task A_forged_Discord_link_is_not_found()
    {
        var response = await _portal.Browser().GetAsync(ManyHandsPaths.LinkDiscord("forged"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
