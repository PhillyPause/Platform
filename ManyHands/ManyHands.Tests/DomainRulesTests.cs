using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Workers;

namespace ManyHands.Tests;

/// <summary>
/// The small rules that decide what a volunteer is told: whether there is
/// room, whether we may email them, and what an outbox payload says.
/// </summary>
public sealed class DomainRulesTests
{
    private static ActivityListItem Item(int? capacity, int signups) => new(
        Id: Guid.NewGuid(),
        Title: "Saturday flyering",
        Description: null,
        Kind: ActivityKind.OneTime,
        Location: "Clark Park",
        StartsAt: DateTimeOffset.UtcNow.AddDays(3),
        EndsAt: null,
        MeetingUrl: null,
        SignupCount: signups,
        Capacity: capacity,
        Tags: [TestData.FlyeringLabel]);

    private static Volunteer Volunteer(
        string? email = "someone@example.test",
        bool optIn = true,
        DateTimeOffset? unsubscribedAt = null,
        bool deleted = false,
        VolunteerRole role = VolunteerRole.Member) =>
        new(Guid.NewGuid(), "Test Person", email, DiscordUserId: null, role, DateTimeOffset.UtcNow,
            EmailVerifiedAt: null, optIn, unsubscribedAt, Guid.NewGuid(), Guid.NewGuid(), deleted);

    [Fact]
    public void No_capacity_means_unlimited_not_full()
    {
        var item = Item(capacity: null, signups: 500);

        item.IsFull.Should().BeFalse();
        item.SpotsRemaining.Should().BeNull();
    }

    [Theory]
    [InlineData(8, 3, 5, false)]
    [InlineData(8, 8, 0, true)]
    // A capacity lowered after people signed up can put us past it.
    [InlineData(4, 7, 0, true)]
    public void Room_left_is_reported_exactly(int capacity, int signups, int left, bool full)
    {
        var item = Item(capacity, signups);

        item.SpotsRemaining.Should().Be(left);
        item.IsFull.Should().Be(full);
    }

    [Fact]
    public void A_new_volunteer_can_be_emailed() =>
        Volunteer().CanReceiveEmail.Should().BeTrue();

    [Fact]
    public void Unsubscribing_opting_out_or_deletion_stops_email()
    {
        Volunteer(unsubscribedAt: DateTimeOffset.UtcNow).CanReceiveEmail.Should().BeFalse();
        Volunteer(optIn: false).CanReceiveEmail.Should().BeFalse();
        Volunteer(deleted: true).CanReceiveEmail.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_usable_address_means_no_email(string? email) =>
        Volunteer(email: email).CanReceiveEmail.Should().BeFalse();

    [Fact]
    public void Each_role_includes_the_ones_below_it()
    {
        var admin = Volunteer(role: VolunteerRole.Admin);
        var member = Volunteer();

        admin.IsAtLeast(VolunteerRole.Organizer).Should().BeTrue();
        member.IsAtLeast(VolunteerRole.Organizer).Should().BeFalse();
        Volunteer(role: VolunteerRole.Admin, deleted: true).IsAtLeast(VolunteerRole.Member)
            .Should().BeFalse("a deleted volunteer can do nothing");
    }

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("first.last@mail.example.test", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("two@@example.test", false)]
    [InlineData("@example.test", false)]
    [InlineData("someone@localhost", false)]
    [InlineData("some one@example.test", false)]
    public void Email_addresses_are_checked_permissively(string address, bool valid) =>
        EmailAddress.LooksValid(address).Should().Be(valid);

    [Fact]
    public void An_outbox_payload_yields_its_id_or_nothing()
    {
        var id = Guid.NewGuid();

        OutboxDispatcher.ReadGuid($$"""{"signupId":"{{id}}"}""", "signupId").Should().Be(id);
        OutboxDispatcher.ReadGuid("""{"signupId":42}""", "signupId").Should().BeNull();
        OutboxDispatcher.ReadGuid("not json", "signupId").Should().BeNull();
        OutboxDispatcher.ReadGuid("{}", "signupId").Should().BeNull();
    }
}
