using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// Invitations: the link is minted when the email goes, works once, and joins
/// the person under the address it was sent to.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InviteTests(DatabaseFixture db)
{
    private InviteRepository Invites => new(db.DataSource);

    private async Task<(Guid InviteId, string Token, string Email)> SentInviteAsync(string? note = null)
    {
        var email = TestData.NewEmail();
        await Invites.CreateAsync([email], note, invitedBy: null);
        var invite = (await Invites.ListAsync()).First(i => i.Email == email);
        var issued = await Invites.IssueLinkAsync(invite.Id);
        return (invite.Id, issued!.Token, email);
    }

    [Fact]
    public async Task People_who_already_joined_and_non_addresses_are_skipped_and_counted()
    {
        var member = await TestData.VolunteerAsync(db.DataSource);

        var result = await Invites.CreateAsync([TestData.NewEmail(), member.Email!, "nonsense"], null, null);

        result.Should().Be(new InviteBatchResult(Invited: 1, AlreadyMembers: 1, Rejected: 1));
    }

    [Fact]
    public async Task Accepting_joins_under_the_invited_address_and_welcomes_once()
    {
        var (_, token, email) = await SentInviteAsync();
        Assert.NotNull(await Invites.ResolveAsync(token));

        var joined = await Invites.AcceptAsync(token, "Newcomer", [TestData.Tabling]);

        Assert.NotNull(joined);
        Assert.False(joined.AlreadyMember);
        var volunteer = await new VolunteerRepository(db.DataSource).GetByIdAsync(joined.VolunteerId);
        Assert.Equal(email, volunteer!.Email);
        Assert.True(joined.Confirmed);
        Assert.NotNull(volunteer.EmailVerifiedAt);
        Assert.Equal(1, await TestData.CountOutboxWelcomesAsync(db.DataSource, joined.VolunteerId));
        Assert.Equal(0, await TestData.CountOutboxForVolunteerAsync(db.DataSource, OutboxKinds.MemberConfirm, joined.VolunteerId));
    }

    [Fact]
    public async Task An_invite_link_works_once()
    {
        var (_, token, _) = await SentInviteAsync();

        Assert.NotNull(await Invites.AcceptAsync(token, "First", []));
        Assert.Null(await Invites.AcceptAsync(token, "Second", []));
        Assert.Null(await Invites.ResolveAsync(token));
    }

    [Fact]
    public async Task Cancelling_kills_the_link()
    {
        var (inviteId, token, _) = await SentInviteAsync();

        Assert.True(await Invites.RevokeAsync(inviteId));

        Assert.Null(await Invites.ResolveAsync(token));
        Assert.Null(await Invites.IssueLinkAsync(inviteId));
    }

    [Fact]
    public async Task Sending_again_mints_a_new_link_and_kills_the_old_one()
    {
        var (inviteId, oldToken, _) = await SentInviteAsync();

        Assert.True(await Invites.ResendAsync(inviteId));
        var fresh = await Invites.IssueLinkAsync(inviteId);

        Assert.Null(await Invites.ResolveAsync(oldToken));
        Assert.NotNull(await Invites.ResolveAsync(fresh!.Token));
    }

    [Fact]
    public async Task The_invite_email_carries_the_link_and_the_note()
    {
        const string note = "Great meeting you at the market!";
        var email = TestData.NewEmail();
        await Invites.CreateAsync([email], note, invitedBy: null);
        var inviteId = (await Invites.ListAsync()).First(i => i.Email == email).Id;

        await TestData.Dispatcher(db.DataSource).DispatchAsync(
            TestData.Message(OutboxKinds.InviteSent, new { inviteId }), CancellationToken.None);

        Assert.Equal(["Join PhillyPause"], await TestData.SubjectsSentToAsync(db.DataSource, email));
        Assert.Equal(InviteState.Sent, (await Invites.ListAsync()).First(i => i.Id == inviteId).StateAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_invite_expires_at_its_boundary_and_not_before()
    {
        var expires = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var invite = new Invite(Guid.NewGuid(), "a@example.test", null, null,
            expires.AddDays(-14), expires, SentAt: expires.AddDays(-14), AcceptedAt: null, RevokedAt: null);

        invite.StateAt(expires.AddSeconds(-1)).Should().Be(InviteState.Sent);
        invite.StateAt(expires).Should().Be(InviteState.Expired);
        (invite with { RevokedAt = expires.AddDays(-1) }).StateAt(expires.AddDays(-2)).Should().Be(InviteState.Revoked);
    }
}
