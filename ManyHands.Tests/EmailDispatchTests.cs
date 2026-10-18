using Dapper;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Email;
using ManyHands.Data.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManyHands.Tests;

/// <summary>
/// From an outbox row to a recorded email, through the real dispatcher and
/// the logging sender, plus the flood cap that stops anyone using signup as a
/// way to bomb somebody's inbox.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EmailDispatchTests(DatabaseFixture db)
{
    private sealed record SentRow(string Subject, string BodyText, EmailStatus Status);

    private sealed record StatusCount(EmailStatus Status, int Count);

    private EmailDispatcher Email() => new(
        new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
        new EmailMessageRepository(db.DataSource),
        Options.Create(new EmailOptions()),
        NullLogger<EmailDispatcher>.Instance);

    private OutboxDispatcher Dispatcher() => new(
        new NotificationQueries(db.DataSource),
        new VolunteerRepository(db.DataSource),
        Email(),
        NullLogger<OutboxDispatcher>.Instance);

    private async Task<IReadOnlyList<SentRow>> SentToAsync(string address)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<SentRow>(
            "SELECT subject, body_text, status FROM email_messages WHERE to_address = @address ORDER BY created_at",
            new { address });
        return rows.AsList();
    }

    private static OutboxMessage Message(string kind, object payload) =>
        new(0, kind, System.Text.Json.JsonSerializer.Serialize(payload), Attempts: 1);

    [Fact]
    public async Task A_signup_confirmation_reaches_the_volunteer()
    {
        var activity = await TestData.ActivityAsync(db.DataSource, "Saturday flyering");
        var email = TestData.NewEmail();
        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Maya", email, []));

        await Dispatcher().DispatchAsync(
            Message(OutboxKinds.SignupConfirmed, new { signupId = ok.SignupId }), CancellationToken.None);

        var sent = Assert.Single(await SentToAsync(email));
        Assert.Equal("Signed up: Saturday flyering", sent.Subject);
        Assert.Equal(EmailStatus.Simulated, sent.Status);
        Assert.Contains(ManyHandsPaths.Me, sent.BodyText);
    }

    [Fact]
    public async Task A_reminder_for_a_cancelled_signup_is_not_sent()
    {
        var activity = await TestData.ActivityAsync(db.DataSource, startsInHours: 2);
        var email = TestData.NewEmail();
        var signups = new SignupRepository(db.DataSource);
        var ok = Assert.IsType<SignupResult.Ok>(await signups.SignUpAsync(activity, "Maya", email, []));
        await signups.CancelAsync(ok.SignupId, ok.VolunteerId);

        await Dispatcher().DispatchAsync(
            Message(OutboxKinds.ActivityReminder, new { signupId = ok.SignupId }), CancellationToken.None);

        Assert.Empty(await SentToAsync(email));
    }

    [Fact]
    public async Task An_unsubscribed_volunteer_hears_nothing()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();
        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Maya", email, []));
        var volunteers = new VolunteerRepository(db.DataSource);
        await volunteers.UnsubscribeByCommsKeyAsync((await volunteers.GetByIdAsync(ok.VolunteerId))!.CommsKey);

        await Dispatcher().DispatchAsync(
            Message(OutboxKinds.SignupConfirmed, new { signupId = ok.SignupId }), CancellationToken.None);

        Assert.Empty(await SentToAsync(email));
    }

    [Fact]
    public async Task The_reminder_sweep_finds_a_signup_once()
    {
        var activity = await TestData.ActivityAsync(db.DataSource, startsInHours: 2);
        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Maya", TestData.NewEmail(), []));
        var notifications = new NotificationQueries(db.DataSource);

        Assert.Contains(ok.SignupId, await notifications.GetDueRemindersAsync(ReminderWorker.Lookahead));

        await new OutboxRepository(db.DataSource).QueueAsync(OutboxKinds.ActivityReminder, new { signupId = ok.SignupId });

        Assert.DoesNotContain(ok.SignupId, await notifications.GetDueRemindersAsync(ReminderWorker.Lookahead));
    }

    [Fact]
    public async Task The_flood_cap_stops_a_confirmation_bomb_and_records_the_suppression()
    {
        const int pastTheCap = 5;
        var email = Email();
        var address = TestData.NewEmail();

        for (var i = 0; i < EmailDispatcher.FloodCap + pastTheCap; i++)
            await email.SendRawAsync(null, address, $"m{i}", "b", "signup.confirmation");

        var byStatus = (await SentToAsync(address))
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(EmailDispatcher.FloodCap, byStatus[EmailStatus.Simulated]);
        Assert.Equal(pastTheCap, byStatus[EmailStatus.Suppressed]);
    }

    [Fact]
    public async Task One_capped_address_does_not_affect_another()
    {
        var email = Email();
        var noisy = TestData.NewEmail();

        for (var i = 0; i <= EmailDispatcher.FloodCap; i++)
            await email.SendRawAsync(null, noisy, $"m{i}", "b", "signup.confirmation");

        Assert.Equal(EmailStatus.Simulated,
            await email.SendRawAsync(null, TestData.NewEmail(), "hello", "b", "signup.confirmation"));
    }
}
