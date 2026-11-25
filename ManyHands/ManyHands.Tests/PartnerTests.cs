using System.Net;
using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>Reading the Partner Tracker sheet's words, and the never-contact rule.</summary>
public sealed class PartnerSheetTests
{
    [Fact]
    public void Text_copied_from_a_sheet_is_tab_separated()
    {
        var rows = DelimitedText.Parse("Organization\tPriority\nIGDA Philly\tB\r\nCode for Philly\tC\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["IGDA Philly", "B"], rows[1]);
    }

    [Fact]
    public void A_csv_keeps_commas_and_quotes_inside_quoted_cells()
    {
        var rows = DelimitedText.Parse("Name,Why\n\"Pen & Pencil Club\",\"Press club, open to all; \"\"public\"\" events\"\n");

        Assert.Equal("Press club, open to all; \"public\" events", rows[1][1]);
    }

    [Theory]
    [InlineData("?src=newsguild", "newsguild")]
    [InlineData("src=newsguild", "newsguild")]
    [InlineData("NewsGuild", "newsguild")]
    [InlineData("", null)]
    public void A_tracking_code_cell_gives_the_code(string cell, string? expected) =>
        Assert.Equal(expected, PartnerSheet.Code(cell));

    [Theory]
    [InlineData("AI safety", Audience.AiRisk)]
    [InlineData("EA", Audience.AiRisk)]
    [InlineData("Labor / journalism", Audience.Labor)]
    [InlineData("Creative / tech", Audience.Labor)]
    [InlineData("Campus / library", Audience.Campus)]
    [InlineData("Parents", Audience.Parents)]
    [InlineData("Faith / venue", Audience.Civic)]
    public void A_category_picks_the_template_group(string category, Audience expected) =>
        Assert.Equal(expected, PartnerSheet.AudienceFor(category));

    [Theory]
    [InlineData("PauseAI US", null)]
    [InlineData("pauseai us", null)]
    [InlineData("Someone else", "https://www.pauseai-us.org")]
    public void PauseAI_US_is_never_contacted(string name, string? website) =>
        Assert.True(NeverContact.Matches(name, website));

    [Fact]
    public void Other_organisations_may_be() => Assert.False(NeverContact.Matches("PauseAI (Philly) Meetup group", "https://www.meetup.com/pauseai-philly"));

    [Fact]
    public void The_next_step_is_one_follow_up_a_week_after_the_first_email()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("Write the first email", PartnerText.NextStep(OutreachStatus.NotContacted, null, now));
        Assert.StartsWith("Follow up ", PartnerText.NextStep(OutreachStatus.Contacted, now.AddDays(-2), now));
        Assert.Equal("Follow up now", PartnerText.NextStep(OutreachStatus.Contacted, now.AddDays(-8), now));
        Assert.Equal(string.Empty, PartnerText.NextStep(OutreachStatus.NoReply, now.AddDays(-20), now));
    }
}

/// <summary>Partners in the CRM: the import, what each partner has brought, and the pages.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PartnerTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string PartnersHeader =
        "Organization\tPriority\tStatus\tCategory\tContact channel\tWhy they fit\tLink\tFound via (org link)\tTracking code\tNotes";

    private PortalFactory _portal = null!;

    private PartnerRepository Partners => new(db.DataSource);

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    private static string NewCode() => $"t{Guid.NewGuid():N}"[..12];

    private static string PartnerRow(string name, string priority, string category, string code, string link = "https://example.test") =>
        $"{name}\t{priority}\tNot contacted\t{category}\tWebsite\tThey care about AI.\t{link}\tA search\t?src={code}\t";

    private async Task<Guid> OrganizationIdAsync(string name)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM crm_organizations WHERE lower(name) = lower(@name) AND NOT is_deleted", new { name });
    }

    private async Task<HttpClient> SignedInAsync(VolunteerRole role)
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, "Partner Person", role);
        var browser = _portal.Browser();
        await Forms.SignInAsync(browser, db.DataSource, volunteer.Id);
        return browser;
    }

    [Fact]
    public async Task The_sheet_imports_once_and_PauseAI_US_is_refused()
    {
        var guild = Unique("Writers Guild");
        var club = Unique("Safe AI Club");
        var (guildCode, clubCode) = (NewCode(), NewCode());
        var sheet = string.Join('\n', PartnersHeader,
            "PauseAI US\tA\tNot contacted\tAI safety\tContact form\tNational.\thttps://www.pauseai-us.org\tProspectus\t?src=pauseaius\t",
            PartnerRow(guild, "B", "Labor / creative", guildCode),
            PartnerRow(club, "A", "Campus", clubCode));

        var first = await Partners.ImportAsync(sheet, null, null);
        var again = await Partners.ImportAsync(sheet, null, null);

        Assert.Equal(2, first.Added);
        Assert.Equal(["PauseAI US"], first.Refused);
        Assert.Equal(0, again.Added);
        Assert.Equal(2, again.Updated);
        var partner = await Partners.GetPartnerAsync(await OrganizationIdAsync(club));
        Assert.Equal("A", partner!.Priority);
        Assert.Equal(OutreachStatus.NotContacted, partner.OutreachStatus);
        Assert.Equal(Audience.Campus, partner.Audience);
        Assert.Equal(clubCode, partner.TrackingCode);
        Assert.Equal("A search", partner.FoundVia);
        Assert.Equal(Guid.Empty, await OrganizationIdAsync("PauseAI US"));
    }

    [Fact]
    public async Task Importing_again_keeps_what_an_organizer_has_set()
    {
        var name = Unique("Library Freedom");
        var sheet = string.Join('\n', PartnersHeader, PartnerRow(name, "B", "Library network", NewCode()));
        await Partners.ImportAsync(sheet, null, null);
        var id = await OrganizationIdAsync(name);
        var partner = await Partners.GetPartnerAsync(id);
        await Partners.SavePartnerAsync(id, new PartnerFields("A", OutreachStatus.Sharing, Audience.Civic, AskRung.Table,
            partner!.Category, partner.TrackingCode, partner.ContactChannel, partner.FoundVia, partner.WhyTheyFit));

        await Partners.ImportAsync(sheet, null, null);

        var after = await Partners.GetPartnerAsync(id);
        Assert.Equal(OutreachStatus.Sharing, after!.OutreachStatus);
        Assert.Equal("A", after.Priority);
    }

    [Fact]
    public async Task A_code_another_partner_holds_is_left_off_and_said_so()
    {
        var code = NewCode();
        var (first, second) = (Unique("First Holder"), Unique("Second Claimant"));

        var report = await Partners.ImportAsync(
            string.Join('\n', PartnersHeader, PartnerRow(first, "B", "Civic", code), PartnerRow(second, "B", "Civic", code)), null, null);

        Assert.Equal(2, report.Added);
        Assert.Contains(report.Problems, p => p.Contains(second, StringComparison.Ordinal));
        Assert.Null((await Partners.GetPartnerAsync(await OrganizationIdAsync(second)))!.TrackingCode);
    }

    [Fact]
    public async Task Links_join_organizations_and_keep_a_name_for_anything_else()
    {
        var (club, meetup) = (Unique("Press Club"), Unique("Reporters Meetup"));
        await Partners.ImportAsync(
            string.Join('\n', PartnersHeader, PartnerRow(club, "A", "Venue / journalism", NewCode()), PartnerRow(meetup, "B", "Journalism", NewCode())),
            null, null);
        var links = string.Join('\n', "From\tConnection\tTo\tSource",
            $"{club}\thosts meetups of\t{meetup}\thttps://example.test/a",
            $"{meetup}\ttestified at\tCity Council hearing\thttps://example.test/b",
            "Somebody\tmet\tSomebody else\thttps://example.test/c");

        var report = await Partners.ImportAsync(null, links, null);
        var again = await Partners.ImportAsync(null, links, null);

        Assert.Equal(2, report.LinksAdded);
        Assert.Single(report.Problems);
        Assert.Equal(0, again.LinksAdded);
        var meetupLinks = await Partners.GetLinksAsync(await OrganizationIdAsync(meetup));
        Assert.Contains(meetupLinks, l => !l.Outgoing && l.OtherName == club && l.Connection == "hosts meetups of");
        Assert.Contains(meetupLinks, l => l.Outgoing && l.OtherId is null && l.OtherName == "City Council hearing");
    }

    [Fact]
    public async Task A_partner_counts_who_its_link_brought()
    {
        var code = NewCode();
        var name = Unique("Counting Partner");
        await Partners.ImportAsync(string.Join('\n', PartnersHeader, PartnerRow(name, "B", "Civic", code)), null, null);
        var volunteers = new VolunteerRepository(db.DataSource);
        await volunteers.JoinAsync("Brought One", TestData.NewEmail(), [], source: code);
        var two = Assert.IsType<JoinResult.Ok>(await volunteers.JoinAsync("Brought Two", TestData.NewEmail(), [], source: code));
        await using (var conn = await db.DataSource.OpenConnectionAsync())
            await conn.ExecuteAsync("UPDATE volunteers SET email_verified_at = now() WHERE id = @id", new { id = two.VolunteerId });

        var partner = (await Partners.GetPartnersAsync()).Single(p => p.Name == name);
        var referred = await Partners.GetReferredAsync(code);

        Assert.Equal(2, partner.Members);
        Assert.Equal(1, partner.ConfirmedMembers);
        Assert.Equal(2, referred.Count);
    }

    [Fact]
    public async Task The_goal_counts_confirmed_members_by_what_brought_them()
    {
        var before = await Partners.GetGoalAsync();
        var code = NewCode();
        await Partners.ImportAsync(string.Join('\n', PartnersHeader, PartnerRow(Unique("Goal Partner"), "C", "Civic", code)), null, null);
        var viaPartner = await TestData.VolunteerAsync(db.DataSource, "Via Partner", confirmed: true);
        await using (var conn = await db.DataSource.OpenConnectionAsync())
            await conn.ExecuteAsync("UPDATE volunteers SET source = @code WHERE id = @id", new { code, id = viaPartner.Id });

        var after = await Partners.GetGoalAsync();

        Assert.Equal(before.Confirmed + 1, after.Confirmed);
        Assert.Equal(before.ViaPartners + 1, after.ViaPartners);
    }

    [Fact]
    public async Task Saving_refuses_a_code_that_is_taken_and_PauseAI_US()
    {
        var code = NewCode();
        var (holder, other) = (Unique("Code Holder"), Unique("Code Wanter"));
        await Partners.ImportAsync(
            string.Join('\n', PartnersHeader, PartnerRow(holder, "B", "Civic", code), PartnerRow(other, "B", "Civic", NewCode())), null, null);
        var crm = new CrmRepository(db.DataSource);
        var pause = await crm.CreateOrganizationAsync(
            new OrganizationFields(OrganizationKind.PartnerOrg, "PauseAI US", null, null, null, null, null), null, null);
        var fields = new PartnerFields("B", OutreachStatus.NotContacted, Audience.Civic, null, null, code, null, null, null);

        Assert.Equal(PartnerSaveResult.CodeTaken, await Partners.SavePartnerAsync(await OrganizationIdAsync(other), fields));
        Assert.Equal(PartnerSaveResult.NeverContact, await Partners.SavePartnerAsync(pause, fields with { TrackingCode = NewCode() }));
    }

    [Theory]
    [InlineData(ManyHandsPaths.TeamPartners)]
    [InlineData(ManyHandsPaths.TeamPartnersImport)]
    [InlineData(Website.Portal.Volunteer.TeamEndpoints.PartnersPath)]
    public async Task A_member_gets_nothing(string path)
    {
        var member = await SignedInAsync(VolunteerRole.Member);

        var response = await member.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_organizer_imports_from_the_page_and_sees_the_partners()
    {
        var name = Unique("Pasted Partner");
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var result = await Forms.SubmitAsync(organizer, ManyHandsPaths.TeamPartnersImport, "import",
            ("Input.Partners", string.Join('\n', PartnersHeader, PartnerRow(name, "A", "AI safety", NewCode()))));
        var page = await organizer.GetStringAsync(ManyHandsPaths.TeamPartners);

        Assert.Contains("1 added", await result.Content.ReadAsStringAsync());
        Assert.Contains("of 1,000 confirmed members", page);
    }

    [Fact]
    public async Task The_partners_file_defuses_a_name_that_is_a_formula()
    {
        const string formula = "=HYPERLINK(\"https://example.test\")";
        await Partners.ImportAsync(string.Join('\n', PartnersHeader, PartnerRow(formula, "C", "Civic", NewCode())), null, null);
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var csv = await organizer.GetStringAsync(Website.Portal.Volunteer.TeamEndpoints.PartnersPath);

        Assert.StartsWith("﻿Organization,Priority,Status,", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"https://example.test\"\")\"", csv);
    }

    [Fact]
    public async Task A_partners_page_shows_their_link_and_its_code()
    {
        var code = NewCode();
        var name = Unique("Linked Partner");
        await Partners.ImportAsync(string.Join('\n', PartnersHeader, PartnerRow(name, "A", "AI safety", code)), null, null);
        var organizer = await SignedInAsync(VolunteerRole.Organizer);

        var page = await organizer.GetStringAsync(ManyHandsPaths.CrmOrganization(await OrganizationIdAsync(name)));

        Assert.Contains($"/?src={code}", page);
        Assert.Contains("<svg", page);
        Assert.Contains("Write the first email", page);
    }
}
