using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>What counts as a tracking code, and how one rides on a link.</summary>
public sealed class TrackingCodeTests
{
    [Theory]
    [InlineData("newsguild", "newsguild")]
    [InlineData("  NewsGuild ", "newsguild")]
    [InlineData("fl-southphilly", "fl-southphilly")]
    [InlineData("rally-2027", "rally-2027")]
    public void A_code_is_lower_case_letters_digits_and_dashes(string value, string expected)
    {
        var code = TrackingCode.Normalize(value);

        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("news guild")]
    [InlineData("news_guild")]
    [InlineData("<script>")]
    [InlineData("a/b")]
    [InlineData("=HYPERLINK")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789-abcd")]
    public void Anything_else_is_no_code_at_all(string? value)
    {
        var code = TrackingCode.Normalize(value);

        Assert.Null(code);
    }

    [Theory]
    [InlineData("/join", "newsguild", "/join?src=newsguild")]
    [InlineData("/events?type=protests", "newsguild", "/events?type=protests&src=newsguild")]
    [InlineData("/join", null, "/join")]
    public void A_code_rides_on_a_path(string path, string? code, string expected)
    {
        var link = TrackingCode.Append(path, code);

        Assert.Equal(expected, link);
    }
}

/// <summary>Where people came from, recorded as they join and sign up, through the real host.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrackingCodeFlowTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string Code = "newsguild";

    private PortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _portal = new PortalFactory(db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _portal.DisposeAsync();

    private async Task<string?> SourceOfAsync(string email)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT source FROM volunteers WHERE lower(email) = lower(@email)", new { email });
    }

    [Fact]
    public async Task Joining_from_a_tracked_link_records_the_code()
    {
        var email = TestData.NewEmail();
        var browser = _portal.Browser();

        var page = await browser.GetStringAsync($"/?{TrackingCode.QueryName}={Code}");
        await Forms.SubmitAsync(browser, $"{ManyHandsPaths.Join}?{TrackingCode.QueryName}={Code}", "join",
            ("Input.DisplayName", "Tracked Joiner"), ("Input.Email", email));

        Assert.Contains($"action=\"{ManyHandsPaths.Join}?src={Code}#join-form\"", page);
        Assert.Equal(Code, await SourceOfAsync(email));
    }

    [Fact]
    public async Task A_mangled_code_is_ignored_and_the_person_still_joins()
    {
        var email = TestData.NewEmail();

        await Forms.SubmitAsync(_portal.Browser(), $"{ManyHandsPaths.Join}?src=%3Cscript%3E", "join",
            ("Input.DisplayName", "Mangled Link"), ("Input.Email", email));

        Assert.NotNull(await new VolunteerRepository(db.DataSource).GetByEmailAsync(email));
        Assert.Null(await SourceOfAsync(email));
    }

    [Fact]
    public async Task The_first_link_counts_and_joining_again_does_not_change_it()
    {
        var email = TestData.NewEmail();
        var volunteers = new VolunteerRepository(db.DataSource);

        await volunteers.JoinAsync("First Touch", email, [], source: Code);
        await volunteers.JoinAsync("First Touch", email, [], source: "reddit");

        Assert.Equal(Code, await SourceOfAsync(email));
    }

    [Fact]
    public async Task Someone_already_on_the_list_is_not_given_a_code_by_joining_again()
    {
        var email = TestData.NewEmail();
        var volunteers = new VolunteerRepository(db.DataSource);

        await volunteers.JoinAsync("Untracked", email, []);
        await volunteers.JoinAsync("Untracked", email, [], source: Code);

        Assert.Null(await SourceOfAsync(email));
    }

    [Fact]
    public async Task An_activity_signup_from_a_tracked_link_records_it_on_the_signup_and_the_new_person()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();
        var browser = _portal.Browser();

        await Forms.SubmitAsync(browser, $"{ManyHandsPaths.Activity(activity)}?{TrackingCode.QueryName}={Code}", "signup",
            ("Input.DisplayName", "Rally Goer"), ("Input.Email", email));

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var referral = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT g.referral FROM signups g JOIN volunteers v ON v.id = g.volunteer_id
            WHERE g.activity_id = @activity AND lower(v.email) = lower(@email)
            """, new { activity, email });
        Assert.Equal(Code, referral);
        Assert.Equal(Code, await SourceOfAsync(email));
    }

    [Fact]
    public async Task The_events_list_carries_the_code_on_to_each_activity()
    {
        var title = $"Tracked {Guid.NewGuid():N}";
        var activity = await TestData.ActivityAsync(db.DataSource, title);

        var page = await _portal.Browser().GetStringAsync($"{ManyHandsPaths.Events}?{TrackingCode.QueryName}={Code}");

        Assert.Contains($"href=\"{ManyHandsPaths.Activity(activity)}?src={Code}\"", page);
    }

    [Fact]
    public async Task The_personal_download_includes_the_link_they_came_by()
    {
        var email = TestData.NewEmail();
        var joined = Assert.IsType<JoinResult.Ok>(
            await new VolunteerRepository(db.DataSource).JoinAsync("Exporter", email, [], source: Code));

        var export = await new VolunteerDataRepository(db.DataSource).ExportAsync(joined.VolunteerId);

        Assert.Equal(Code, export!.JoinedVia);
    }
}
