using System.Net;
using Dapper;
using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The outreach log behind the action centre's "I called" and "I sent it":
/// what it keeps, who sees it, and that a person's own record is theirs to
/// export and to have forgotten.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutreachTests(DatabaseFixture db) : IAsyncLifetime
{
    private PortalFactory _portal = null!;

    // A real file in Content/_officials: the pages 404 an official they do not know.
    private const string Official = "john-fetterman";

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private OutreachRepository Repo => new(db.DataSource);

    private async Task<(Volunteer Volunteer, HttpClient Browser)> SignedInAsync(VolunteerRole role, string name = "Outreach Person")
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, name, role);
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, volunteer.Id);
        return (volunteer, browser);
    }

    [Fact]
    public async Task A_logged_action_is_the_volunteers_own_and_keeps_no_more_than_was_said()
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, "Logger");
        await Repo.LogAsync(volunteer.Id, "key-a", "Official A", OutreachChannel.Call, OutreachOutcome.LeftVoicemail, "  Busy line  ");
        await Repo.LogAsync(null, "key-a", "Official A", OutreachChannel.Email, OutreachOutcome.Sent, null);

        var mine = await Repo.ForVolunteerAsync(volunteer.Id);

        mine.Should().ContainSingle();
        mine[0].Channel.Should().Be(OutreachChannel.Call);
        mine[0].Outcome.Should().Be(OutreachOutcome.LeftVoicemail);
        mine[0].Note.Should().Be("Busy line");
        mine[0].VolunteerName.Should().Be("Logger");
    }

    [Fact]
    public async Task A_long_note_is_cut_to_the_limit_rather_than_refused()
    {
        var id = await Repo.LogAsync(null, "key-long", "Official Long", OutreachChannel.Email, null, new string('x', 900));

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var length = await conn.ExecuteScalarAsync<int>("SELECT length(note) FROM outreach_actions WHERE id = @id", new { id });
        length.Should().Be(OutreachRepository.NoteLimit);
    }

    [Fact]
    public async Task The_organizers_numbers_count_people_officials_and_the_anonymous()
    {
        var key = $"key-{Guid.NewGuid():N}";
        var a = await TestData.VolunteerAsync(db.DataSource, "Counted A");
        await Repo.LogAsync(a.Id, key, "Counted Official", OutreachChannel.Call, OutreachOutcome.SpokeToSomeone, null);
        await Repo.LogAsync(a.Id, key, "Counted Official", OutreachChannel.Email, OutreachOutcome.Sent, null);
        await Repo.LogAsync(null, key, "Counted Official", OutreachChannel.ContactForm, OutreachOutcome.Sent, null);

        var row = (await Repo.ByOfficialAsync()).Single(r => r.OfficialKey == key);

        row.Actions.Should().Be(3);
        row.Calls.Should().Be(1);
        row.Emails.Should().Be(2, "an email and a contact form are both messages");
        row.People.Should().Be(1, "the anonymous action is nobody's");

        var totals = await Repo.TotalsAsync();
        totals.ThisWeek.Should().BeGreaterThanOrEqualTo(3);
        totals.AnonymousLast30Days.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Export_includes_outreach_and_deletion_keeps_the_count_but_not_the_notes()
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, "Exported");
        var id = await Repo.LogAsync(volunteer.Id, "key-x", "Official X", OutreachChannel.Call, OutreachOutcome.NoAnswer, "Tried twice");
        var data = new VolunteerDataRepository(db.DataSource);

        var export = await data.ExportAsync(volunteer.Id);
        export!.Outreach.Should().ContainSingle(o => o.Official == "Official X" && o.Channel == "Call" && o.Note == "Tried twice");

        (await data.DeleteAsync(volunteer.Id)).Should().BeTrue();

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<KeptRow>(
            "SELECT volunteer_id, note FROM outreach_actions WHERE id = @id", new { id });
        row.VolunteerId.Should().Be(volunteer.Id, "the action still happened");
        row.Note.Should().BeNull("nothing the person wrote stays");
    }

    [Fact]
    public async Task I_called_on_the_call_page_is_logged_as_the_signed_in_volunteers()
    {
        var (volunteer, browser) = await SignedInAsync(VolunteerRole.Member, "Caller");

        var response = await Forms.SubmitAsync(browser, $"/act/call/{Official}", "log",
            ("Input.Outcome", nameof(OutreachOutcome.SpokeToSomeone)),
            ("Input.Note", "Staffer was friendly"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("That counts");

        var mine = await Repo.ForVolunteerAsync(volunteer.Id);
        mine.Should().ContainSingle(a => a.OfficialKey == Official && a.Channel == OutreachChannel.Call
                                         && a.Outcome == OutreachOutcome.SpokeToSomeone && a.Note == "Staffer was friendly");

        var page = await browser.GetStringAsync(ManyHandsPaths.MyOutreach);
        page.Should().Contain("Your outreach").And.Contain("Staffer was friendly");
    }

    [Fact]
    public async Task Someone_not_signed_in_is_counted_with_no_name_and_offered_joining()
    {
        var browser = _portal.Browser();
        var before = (await Repo.TotalsAsync()).AllTime;

        var response = await Forms.SubmitAsync(browser, $"/act/call/{Official}", "log",
            ("Input.Outcome", nameof(OutreachOutcome.LeftVoicemail)));

        var page = await response.Content.ReadAsStringAsync();
        page.Should().Contain("with nobody&rsquo;s name on it").And.Contain("href=\"/join\"");
        (await Repo.TotalsAsync()).AllTime.Should().Be(before + 1);
    }

    [Fact]
    public async Task The_outreach_page_and_its_download_are_for_organizers_only()
    {
        var (_, member) = await SignedInAsync(VolunteerRole.Member);
        (await member.GetAsync(ManyHandsPaths.TeamOutreach)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (_, organizer) = await SignedInAsync(VolunteerRole.Organizer, "Outreach Organizer");
        await Repo.LogAsync(null, "key-csv", "=HYPERLINK(\"x\")", OutreachChannel.Email, OutreachOutcome.Sent, "@SUM(1)");

        var page = await organizer.GetStringAsync(ManyHandsPaths.TeamOutreach);
        page.Should().Contain("By official").And.Contain("Nobody has reached these yet");

        var csv = await organizer.GetStringAsync(ManyHandsPaths.TeamOutreachCsv);
        csv.Should().Contain("'=HYPERLINK").And.Contain("'@SUM(1)").And.NotContain(",=HYPERLINK");
    }

    [Fact]
    public async Task With_ManyHands_on_the_write_page_offers_I_sent_it()
    {
        var page = await _portal.Browser().GetStringAsync($"/act/write/{Official}");

        page.Should().Contain("Sent it? Tell us").And.Contain("name=\"_handler\" value=\"log\"");
    }

    // Dapper maps records, never value tuples (MANYHANDS.md section 4).
    private sealed record KeptRow(Guid? VolunteerId, string? Note);
}
