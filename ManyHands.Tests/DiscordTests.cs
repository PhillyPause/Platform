using Dapper;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Discord;
using ManyHands.Data.Workers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManyHands.Tests;

/// <summary>
/// The bot's side of the outbox, with a recording sender in place of Discord:
/// the announcement and its thread exactly once, interest pings by DM or by
/// email but never both, the signup DM and thread note, linking, and the role
/// sweep.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DiscordTests(DatabaseFixture db)
{
    private const ulong RoleId = 800_000_000_000_000_001;

    private DiscordQueries Queries => new(db.DataSource);

    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1L << 50, 1L << 60);

    private async Task<Guid> PublishedAsync(string title, params short[] tags) =>
        await TestData.ActivityAsync(db.DataSource, $"{title} {Guid.NewGuid():N}", tags: tags);

    private async Task<Volunteer> LinkedVolunteerAsync(string name, params short[] interests)
    {
        var volunteer = await TestData.VolunteerAsync(db.DataSource, name, interests: interests);
        var discordId = NewDiscordId();
        await Queries.LinkAsync(volunteer.Id, discordId);
        return volunteer with { DiscordUserId = discordId };
    }

    private static OutboxMessage Published(Guid activityId) =>
        TestData.Message(OutboxKinds.ActivityPublished, new { activityId });

    private static OutboxMessage Confirmed(Guid signupId) =>
        TestData.Message(OutboxKinds.SignupConfirmed, new { signupId });

    [Fact]
    public async Task Publishing_announces_once_with_a_thread_however_often_it_is_delivered()
    {
        var discord = new RecordingDiscordSender();
        var dispatcher = TestData.Dispatcher(db.DataSource, discord: discord);
        var activity = await PublishedAsync("Leafleting");

        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);
        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);

        var post = Assert.Single(discord.Announcements);
        Assert.Contains(ManyHandsPaths.Activity(activity), post);
        Assert.Equal(RecordingDiscordSender.ThreadId, await Queries.GetAnnouncementThreadAsync(activity));
    }

    [Fact]
    public async Task An_undelivered_announcement_fails_the_row_and_is_posted_on_the_retry()
    {
        var discord = new RecordingDiscordSender { Deliver = false };
        var dispatcher = TestData.Dispatcher(db.DataSource, discord: discord);
        var activity = await PublishedAsync("Leafleting");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(Published(activity), CancellationToken.None));

        discord.Deliver = true;
        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);

        Assert.Single(discord.Announcements);
    }

    [Fact]
    public async Task Interest_pings_go_by_DM_when_linked_and_by_email_when_not_never_both()
    {
        var discord = new RecordingDiscordSender();
        var dispatcher = TestData.Dispatcher(db.DataSource, discord: discord);
        var linked = await LinkedVolunteerAsync("Linked", TestData.Flyering);
        var unlinked = await TestData.VolunteerAsync(db.DataSource, "Unlinked", interests: TestData.Flyering);
        var activity = await PublishedAsync("Flyering", TestData.Flyering);

        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);
        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);

        var dm = Assert.Single(discord.DirectMessages, d => d.DiscordUserId == linked.DiscordUserId);
        Assert.Contains(ManyHandsPaths.Me, dm.Message);
        Assert.Empty(await TestData.SubjectsSentToAsync(db.DataSource, linked.Email!));
        Assert.Single(await TestData.SubjectsSentToAsync(db.DataSource, unlinked.Email!));
    }

    [Fact]
    public async Task Opting_out_of_email_opts_out_of_DMs_too()
    {
        var discord = new RecordingDiscordSender();
        var linked = await LinkedVolunteerAsync("Quiet", TestData.Flyering);
        await new VolunteerRepository(db.DataSource).SetEmailOptInAsync(linked.Id, false);
        var activity = await PublishedAsync("Flyering", TestData.Flyering);

        await TestData.Dispatcher(db.DataSource, discord: discord).DispatchAsync(Published(activity), CancellationToken.None);

        Assert.DoesNotContain(discord.DirectMessages, d => d.DiscordUserId == linked.DiscordUserId);
    }

    [Fact]
    public async Task A_signup_DMs_the_linked_volunteer_and_notes_the_thread_once_naming_nobody()
    {
        var discord = new RecordingDiscordSender();
        var dispatcher = TestData.Dispatcher(db.DataSource, discord: discord);
        var activity = await PublishedAsync("Phone bank");
        await dispatcher.DispatchAsync(Published(activity), CancellationToken.None);

        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Maya Linked", TestData.NewEmail(), []));
        var discordId = NewDiscordId();
        await Queries.LinkAsync(ok.VolunteerId, discordId);

        await dispatcher.DispatchAsync(Confirmed(ok.SignupId), CancellationToken.None);
        await dispatcher.DispatchAsync(Confirmed(ok.SignupId), CancellationToken.None);

        Assert.Contains(discord.DirectMessages, d => d.DiscordUserId == discordId && d.Message.Contains("You are signed up"));
        var note = Assert.Single(discord.ThreadPosts);
        Assert.Equal(RecordingDiscordSender.ThreadId, note.ThreadId);
        Assert.DoesNotContain("Maya", note.Message);
    }

    [Fact]
    public async Task A_Discord_outage_does_not_fail_a_signup_confirmation()
    {
        var discord = new RecordingDiscordSender { Deliver = false };
        var activity = await PublishedAsync("Phone bank");
        var email = TestData.NewEmail();
        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Maya", email, []));
        await Queries.LinkAsync(ok.VolunteerId, NewDiscordId());

        await TestData.Dispatcher(db.DataSource, discord: discord).DispatchAsync(Confirmed(ok.SignupId), CancellationToken.None);

        Assert.Single(await TestData.SubjectsSentToAsync(db.DataSource, email));
    }

    [Fact]
    public async Task Linking_an_account_moves_it_from_whoever_had_it()
    {
        var first = await LinkedVolunteerAsync("First");
        var second = await TestData.VolunteerAsync(db.DataSource, "Second");

        await Queries.LinkAsync(second.Id, first.DiscordUserId!.Value);

        Assert.Equal(second.Id, (await Queries.GetVolunteerByDiscordIdAsync(first.DiscordUserId.Value))!.Id);
        Assert.Null((await new VolunteerRepository(db.DataSource).GetByIdAsync(first.Id))!.DiscordUserId);
    }

    [Fact]
    public void A_link_token_reads_back_and_a_tampered_one_does_not()
    {
        var links = new DiscordLinks(new EphemeralDataProtectionProvider());
        var token = links.Issue(1234567890123, "maya|pa");

        Assert.Equal((1234567890123UL, "maya|pa"), links.Read(token));
        Assert.Null(links.Read(token[..^2] + "xx"));
        Assert.Null(links.Read("not-a-token"));
    }

    [Fact]
    public async Task The_role_sweep_grants_once_and_takes_back_on_unlink()
    {
        var discord = new RecordingDiscordSender();
        var worker = RoleWorker(discord, RoleId);
        var volunteer = await LinkedVolunteerAsync("Role holder");
        var discordId = volunteer.DiscordUserId!.Value;

        await worker.SweepAsync(CancellationToken.None);
        await worker.SweepAsync(CancellationToken.None);
        Assert.Single(discord.RoleChanges, c => c.DiscordUserId == discordId && c.Grant && c.RoleId == RoleId);

        await Queries.UnlinkAsync(volunteer.Id);
        await worker.SweepAsync(CancellationToken.None);
        await worker.SweepAsync(CancellationToken.None);
        Assert.Single(discord.RoleChanges, c => c.DiscordUserId == discordId && !c.Grant);
    }

    [Fact]
    public async Task Deleting_someone_takes_their_role_back_at_the_next_sweep()
    {
        var discord = new RecordingDiscordSender();
        var worker = RoleWorker(discord, RoleId);
        var volunteer = await LinkedVolunteerAsync("Leaving");
        await worker.SweepAsync(CancellationToken.None);

        await new VolunteerDataRepository(db.DataSource).DeleteAsync(volunteer.Id);
        await worker.SweepAsync(CancellationToken.None);

        Assert.Contains(discord.RoleChanges, c => c.DiscordUserId == volunteer.DiscordUserId && !c.Grant);
    }

    [Fact]
    public async Task A_simulated_sweep_records_nothing_so_the_first_real_one_does_the_work()
    {
        var discord = new RecordingDiscordSender { RoleOutcome = RoleSyncOutcome.Simulated };
        var worker = RoleWorker(discord, RoleId);
        var volunteer = await LinkedVolunteerAsync("Not yet");

        await worker.SweepAsync(CancellationToken.None);
        await worker.SweepAsync(CancellationToken.None);

        Assert.Equal(2, discord.RoleChanges.Count(c => c.DiscordUserId == volunteer.DiscordUserId));
    }

    [Fact]
    public async Task With_no_role_configured_the_sweep_only_takes_back_what_it_gave()
    {
        var discord = new RecordingDiscordSender();
        var volunteer = await LinkedVolunteerAsync("Former");
        await RoleWorker(discord, RoleId).SweepAsync(CancellationToken.None);

        await RoleWorker(discord, roleId: null).SweepAsync(CancellationToken.None);

        Assert.Contains(discord.RoleChanges, c => c.DiscordUserId == volunteer.DiscordUserId && !c.Grant);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM discord_role_grants WHERE volunteer_id = @id", new { id = volunteer.Id }));
    }

    private DiscordRoleSyncWorker RoleWorker(IDiscordSender sender, ulong? roleId) => new(
        Queries, sender,
        Options.Create(new DiscordOptions { RoleId = roleId, SendDelay = TimeSpan.Zero }),
        NullLogger<DiscordRoleSyncWorker>.Instance);
}
