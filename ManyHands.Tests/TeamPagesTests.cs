using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The organizers' pages, through the real host: who may see them, and that
/// what they do reaches the database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class TeamPagesTests(DatabaseFixture db) : IAsyncLifetime
{
    private PortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private async Task<HttpClient> SignedInAsync(VolunteerRole role, string name = "Team Person")
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, name, role);
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, volunteer.Id);
        return browser;
    }

    [Fact]
    public async Task Team_pages_send_a_stranger_to_sign_in()
    {
        var response = await _portal.Browser().GetAsync(ManyHandsPaths.Team);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(ManyHandsPaths.SignIn, response.Headers.Location?.AbsolutePath);
    }

    /// <summary>Every team page opens for an organizer: a page that throws fails here, not in front of one.</summary>
    [Theory]
    [InlineData(ManyHandsPaths.Team)]
    [InlineData(ManyHandsPaths.TeamNewActivity)]
    [InlineData(ManyHandsPaths.TeamMembers)]
    [InlineData(ManyHandsPaths.TeamInvites)]
    [InlineData(ManyHandsPaths.Crm)]
    [InlineData(ManyHandsPaths.Crm + "?kind=Venue")]
    [InlineData(ManyHandsPaths.CrmPipeline)]
    [InlineData(ManyHandsPaths.CrmOfficials)]
    [InlineData(ManyHandsPaths.Inbox)]
    [InlineData(ManyHandsPaths.Inbox + "?triage=true")]
    [InlineData(ManyHandsPaths.InboxCompose)]
    [InlineData(ManyHandsPaths.InboxTemplates)]
    public async Task Every_team_page_opens_for_an_organizer(string path)
    {
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var response = await organizer.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_record_page_opens_for_an_organizer()
    {
        var crm = new CrmRepository(db.DataSource);
        var org = await crm.CreateOrganizationAsync(
            new OrganizationFields(OrganizationKind.Venue, $"Venue {Guid.NewGuid():N}", null, null, null, null, null), null, null);
        var engagement = await crm.SaveEngagementAsync(null,
            new EngagementFields(org, "Book the room", EngagementStatus.Outreach, null, null, null, null, null, null));
        var activity = await TestData.ActivityAsync(db.DataSource);
        var thread = Guid.NewGuid();
        await new CrmEmailRepository(db.DataSource).RecordAsync(new CrmEmail(
            Guid.Empty, EmailDirection.Inbound, org, null, null, "someone@example.test", "outreach@example.test",
            "Hello", "Hi.", null, null, thread, EmailStatus.Sent, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, NeedsTriage: true));
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        foreach (var path in new[]
                 {
                     ManyHandsPaths.CrmOrganization(org), ManyHandsPaths.CrmEngagement(engagement),
                     ManyHandsPaths.InboxThread(thread), ManyHandsPaths.TeamActivity(activity),
                     ManyHandsPaths.TeamEditActivity(activity), ManyHandsPaths.TeamCheckInCode(activity)
                 })
        {
            Assert.True((await organizer.GetAsync(path)).StatusCode == HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task To_a_member_the_team_pages_do_not_exist()
    {
        var member = await SignedInAsync(VolunteerRole.Member);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(ManyHandsPaths.Team)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Website.Portal.Volunteer.TeamEndpoints.ParticipationPath)).StatusCode);
    }

    [Fact]
    public async Task An_organizer_drafts_an_activity_and_publishes_it()
    {
        var organizer = await SignedInAsync(VolunteerRole.Organizer);
        var title = $"Drafted {Guid.NewGuid():N}";
        var starts = LocalTime.ToLocal(DateTimeOffset.UtcNow.AddDays(4)).ToString("yyyy-MM-ddTHH:mm");
        var ends = LocalTime.ToLocal(DateTimeOffset.UtcNow.AddDays(4).AddHours(2)).ToString("yyyy-MM-ddTHH:mm");

        var saved = await Forms.SubmitAsync(organizer, ManyHandsPaths.TeamNewActivity, "activity",
            ("Input.Title", title), ("Input.Kind", nameof(ActivityKind.OneTime)),
            ("Input.StartsAt", starts), ("Input.EndsAt", ends), ("interest", TestData.Flyering.ToString()));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var roster = saved.Headers.Location!.PathAndQuery;
        var id = Guid.Parse(roster.Split('/').Last());
        var draft = await new ActivityAdminRepository(db.DataSource).GetAsync(id);
        Assert.Equal(title, draft!.Title);
        Assert.False(draft.IsPublished);
        Assert.Equal([TestData.Flyering], draft.TypeIds);

        await Forms.SubmitAsync(organizer, roster, "publish");

        Assert.NotNull(await new ActivityRepository(db.DataSource).GetPublishedByIdAsync(id));
    }

    [Fact]
    public async Task The_editor_refuses_an_end_before_the_start()
    {
        var organizer = await SignedInAsync(VolunteerRole.Organizer);
        var title = $"Backwards {Guid.NewGuid():N}";

        var response = await Forms.SubmitAsync(organizer, ManyHandsPaths.TeamNewActivity, "activity",
            ("Input.Title", title), ("Input.Kind", nameof(ActivityKind.OneTime)),
            ("Input.StartsAt", "2030-06-01T18:00"), ("Input.EndsAt", "2030-06-01T17:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("It has to end after it starts.", await response.Content.ReadAsStringAsync());
        await using var conn = await db.DataSource.OpenConnectionAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM activities WHERE title = @title", new { title }));
    }

    [Fact]
    public async Task The_door_code_lets_a_volunteer_check_themselves_in_and_a_forged_one_does_not()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();
        await new SignupRepository(db.DataSource).SignUpAsync(activity, "At The Door", email, []);
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var codePage = await organizer.GetStringAsync(ManyHandsPaths.TeamCheckInCode(activity));
        var link = CheckInLink().Match(codePage).Groups[1].Value;
        var path = new Uri(link).PathAndQuery;

        var done = await Forms.SubmitAsync(_portal.Browser(), path, "check-in", ("Input.Email", email));

        Assert.Contains("You are in", await done.Content.ReadAsStringAsync());
        Assert.NotNull(Assert.Single(await new ActivityAdminRepository(db.DataSource).GetRosterAsync(activity)).CheckedInAt);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _portal.Browser().GetAsync(ManyHandsPaths.SelfCheckIn("forged-token"))).StatusCode);
    }

    [Fact]
    public async Task Only_an_admin_can_change_a_role()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Promotable");
        var organizer = await SignedInAsync(VolunteerRole.Organizer);
        var admin = await SignedInAsync(VolunteerRole.Admin, "An Admin");
        var value = $"{target.Id}:{VolunteerRole.Organizer}";
        var volunteers = new VolunteerRepository(db.DataSource);

        await Forms.SubmitAsync(organizer, ManyHandsPaths.TeamMembers, "role", ("Role.Value", value));
        Assert.Equal(VolunteerRole.Member, (await volunteers.GetByIdAsync(target.Id))!.Role);

        await Forms.SubmitAsync(admin, ManyHandsPaths.TeamMembers, "role", ("Role.Value", value));
        Assert.Equal(VolunteerRole.Organizer, (await volunteers.GetByIdAsync(target.Id))!.Role);
    }

    [Fact]
    public async Task A_listed_address_is_an_admin_once_it_signs_in()
    {
        var email = TestData.NewEmail();
        await using var portal = new PortalFactory(db.ConnectionString, ("ManyHands:Admins:0", email));
        var volunteers = new VolunteerRepository(db.DataSource);
        var id = await volunteers.UpsertByEmailAsync(email, "Founder");

        await Forms.SignInAsync(portal.Browser(), db.DataSource, id);

        Assert.Equal(VolunteerRole.Admin, (await volunteers.GetByIdAsync(id))!.Role);
    }

    [Fact]
    public async Task The_participation_file_defuses_a_name_that_is_a_formula()
    {
        const string formula = "=HYPERLINK(\"https://example.test\")";
        var activity = await TestData.ActivityAsync(db.DataSource);
        await new SignupRepository(db.DataSource).SignUpAsync(activity, formula, TestData.NewEmail(), []);
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var csv = await organizer.GetStringAsync(Website.Portal.Volunteer.TeamEndpoints.ParticipationPath);

        Assert.Contains("\"'=HYPERLINK(\"\"https://example.test\"\")\"", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
    }

    [GeneratedRegex("<a href=\"([^\"]+/volunteer/check-in/[^\"]+)\"")]
    private static partial Regex CheckInLink();
}
