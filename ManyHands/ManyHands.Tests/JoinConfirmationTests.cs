using Dapper;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManyHands.Tests;

/// <summary>
/// Joining from the site asks the person to confirm the address is theirs:
/// one email, one reminder a few days on, a link that confirms once, and a
/// removal link that works without signing in.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class JoinConfirmationTests(DatabaseFixture db)
{
    private VolunteerRepository Volunteers => new(db.DataSource);
    private AuthTokenRepository Tokens => new(db.DataSource);

    private async Task<(Guid Id, string Email)> JoinAsync(string name = "Joiner")
    {
        var email = TestData.NewEmail();
        var ok = Assert.IsType<JoinResult.Ok>(await Volunteers.JoinAsync(name, email, [TestData.Flyering]));
        return (ok.VolunteerId, email);
    }

    private async Task DispatchAsync(string kind, Guid volunteerId) =>
        await TestData.Dispatcher(db.DataSource).DispatchAsync(
            TestData.Message(kind, new { volunteerId }), CancellationToken.None);

    [Fact]
    public async Task Joining_asks_for_a_confirmation_and_books_one_reminder_days_later()
    {
        var email = TestData.NewEmail();

        var ok = Assert.IsType<JoinResult.Ok>(await Volunteers.JoinAsync("New", email, [TestData.Flyering]));

        Assert.False(ok.Confirmed);
        Assert.Null((await Volunteers.GetByIdAsync(ok.VolunteerId))!.EmailVerifiedAt);
        Assert.Equal(1, await TestData.CountOutboxForVolunteerAsync(db.DataSource, OutboxKinds.MemberConfirm, ok.VolunteerId));
        Assert.Equal(0, await TestData.CountOutboxWelcomesAsync(db.DataSource, ok.VolunteerId));

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var waits = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT next_attempt_at > now() + interval '2 days' FROM outbox
            WHERE kind = @kind AND payload->>'volunteerId' = @id
            """,
            new { kind = OutboxKinds.MemberNudge, id = ok.VolunteerId.ToString() });
        Assert.True(waits);
    }

    [Fact]
    public async Task Joining_again_before_confirming_resends_the_email_but_not_the_reminder()
    {
        var (id, email) = await JoinAsync();

        var again = Assert.IsType<JoinResult.Ok>(await Volunteers.JoinAsync("Joiner", email, []));

        Assert.True(again.AlreadyMember);
        Assert.False(again.Confirmed);
        Assert.Equal(2, await TestData.CountOutboxForVolunteerAsync(db.DataSource, OutboxKinds.MemberConfirm, id));
        Assert.Equal(1, await TestData.CountOutboxForVolunteerAsync(db.DataSource, OutboxKinds.MemberNudge, id));
    }

    [Fact]
    public async Task The_emailed_link_confirms_once_and_offers_removal()
    {
        var (id, email) = await JoinAsync();
        await DispatchAsync(OutboxKinds.MemberConfirm, id);

        var raw = await TestData.ConfirmTokenSentToAsync(db.DataSource, email);
        Assert.NotNull(raw);
        Assert.Equal([$"One click to finish joining {Organisation.Name}"], await TestData.SubjectsSentToAsync(db.DataSource, email));

        Assert.IsType<MagicLinkResult.Ok>(await Tokens.ConfirmAsync(raw));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.ConfirmAsync(raw));
        Assert.NotNull((await Volunteers.GetByIdAsync(id))!.EmailVerifiedAt);

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var body = await conn.ExecuteScalarAsync<string>(
            "SELECT body_text FROM email_messages WHERE lower(to_address) = lower(@email)", new { email });
        var volunteer = await Volunteers.GetByIdAsync(id);
        Assert.Contains(ManyHandsPaths.Remove(volunteer!.CommsKey), body);
    }

    [Fact]
    public async Task A_confirmation_link_cannot_sign_in_and_a_sign_in_link_cannot_confirm()
    {
        var (id, email) = await JoinAsync();
        await DispatchAsync(OutboxKinds.MemberConfirm, id);
        var confirm = (await TestData.ConfirmTokenSentToAsync(db.DataSource, email))!;
        var signIn = await Tokens.IssueAsync(id);

        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.RedeemAsync(confirm));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.ConfirmAsync(signIn));
    }

    [Fact]
    public async Task Confirming_spends_every_confirmation_link_and_asking_for_another_keeps_the_first()
    {
        var (id, email) = await JoinAsync();
        await DispatchAsync(OutboxKinds.MemberConfirm, id);
        var first = (await TestData.ConfirmTokenSentToAsync(db.DataSource, email))!;
        var second = await Tokens.IssueConfirmationAsync(id);

        Assert.IsType<MagicLinkResult.Ok>(await Tokens.ConfirmAsync(first));
        Assert.IsType<MagicLinkResult.Invalid>(await Tokens.ConfirmAsync(second));
    }

    [Fact]
    public async Task The_reminder_goes_to_the_undecided_and_says_nothing_to_anyone_who_answered()
    {
        var (undecided, undecidedEmail) = await JoinAsync();
        var (confirmed, confirmedEmail) = await JoinAsync();
        await Tokens.ConfirmAsync(await Tokens.IssueConfirmationAsync(confirmed));
        var (removed, _) = await JoinAsync();
        var key = (await Volunteers.GetByIdAsync(removed))!.CommsKey;
        Assert.Equal(RemoveResult.Removed, await new VolunteerDataRepository(db.DataSource).RemoveByCommsKeyAsync(key));

        foreach (var id in new[] { undecided, confirmed, removed })
            await DispatchAsync(OutboxKinds.MemberNudge, id);

        Assert.Equal([$"Still want to hear from {Organisation.Name}?"], await TestData.SubjectsSentToAsync(db.DataSource, undecidedEmail));
        Assert.Empty(await TestData.SubjectsSentToAsync(db.DataSource, confirmedEmail));
        await using var conn = await db.DataSource.OpenConnectionAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM email_messages WHERE volunteer_id = @removed", new { removed }));
    }

    [Fact]
    public async Task Removing_by_the_link_deletes_a_member_but_never_an_organizer()
    {
        var data = new VolunteerDataRepository(db.DataSource);
        var (id, _) = await JoinAsync();
        var member = await Volunteers.GetByIdAsync(id);
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Olu", VolunteerRole.Organizer);

        Assert.Equal(RemoveResult.Removed, await data.RemoveByCommsKeyAsync(member!.CommsKey));
        Assert.Equal(RemoveResult.NotFound, await data.RemoveByCommsKeyAsync(member.CommsKey));
        Assert.Equal(RemoveResult.Organizer, await data.RemoveByCommsKeyAsync(organizer.CommsKey));

        var gone = await Volunteers.GetByIdAsync(id);
        Assert.True(gone!.IsDeleted);
        Assert.Null(gone.Email);
        Assert.False((await Volunteers.GetByIdAsync(organizer.Id))!.IsDeleted);
    }

    [Fact]
    public async Task An_activity_signup_from_a_new_address_offers_the_confirmation_too()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();
        var signup = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Walk Up", email, []));

        await TestData.Dispatcher(db.DataSource).DispatchAsync(
            TestData.Message(OutboxKinds.SignupConfirmed, new { signupId = signup.SignupId }), CancellationToken.None);

        var raw = await TestData.ConfirmTokenSentToAsync(db.DataSource, email);
        Assert.NotNull(raw);
        Assert.IsType<MagicLinkResult.Ok>(await Tokens.ConfirmAsync(raw));
    }

    [Theory]
    [InlineData(OutboxKinds.MemberConfirm)]
    [InlineData(OutboxKinds.MemberNudge)]
    public async Task Both_joining_emails_go_out_with_the_button_and_the_logo(string kind)
    {
        var (id, _) = await JoinAsync();
        var sender = new RecordingEmailSender();
        var options = new EmailOptions();
        var mail = new EmailDispatcher(sender, new EmailMessageRepository(db.DataSource),
            Options.Create(options), NullLogger<EmailDispatcher>.Instance);

        await TestData.Dispatcher(db.DataSource, mail).DispatchAsync(
            TestData.Message(kind, new { volunteerId = id }), CancellationToken.None);

        var sent = Assert.Single(sender.Sent);
        var confirmLink = sent.BodyText.Split('\n').Select(l => l.Trim())
            .Single(l => l.Contains("/volunteer/confirm/", StringComparison.Ordinal));
        Assert.NotNull(sent.BodyHtml);
        Assert.Contains($"href=\"{confirmLink}\"", sent.BodyHtml);
        Assert.Contains($"src=\"{options.PublicBaseUrl}{EmailTemplates.LogoPath}\"", sent.BodyHtml);
    }
}
