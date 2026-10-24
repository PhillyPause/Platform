using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// What the members page does to a person, and who may: the role of whoever
/// asks is read from the database each time, whatever the page offered.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MemberAdminRepositoryTests(DatabaseFixture db)
{
    private MemberAdminRepository Admin =>
        new(db.DataSource, new VolunteerRepository(db.DataSource), new VolunteerDataRepository(db.DataSource));

    private VolunteerRepository Volunteers => new(db.DataSource);

    private Task<int> ConfirmsQueuedAsync(Guid volunteerId) =>
        TestData.CountOutboxForVolunteerAsync(db.DataSource, OutboxKinds.MemberConfirm, volunteerId);

    [Fact]
    public async Task Only_an_admin_can_change_a_role()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Promotable");
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Organizer", VolunteerRole.Organizer);
        var admin = await TestData.VolunteerAsync(db.DataSource, "Admin", VolunteerRole.Admin);

        Assert.Equal(MemberActionResult.NotAllowed, await Admin.SetRoleAsync(organizer.Id, target.Id, VolunteerRole.Organizer));
        Assert.Equal(VolunteerRole.Member, (await Volunteers.GetByIdAsync(target.Id))!.Role);

        Assert.Equal(MemberActionResult.Done, await Admin.SetRoleAsync(admin.Id, target.Id, VolunteerRole.Organizer));
        Assert.Equal(VolunteerRole.Organizer, (await Volunteers.GetByIdAsync(target.Id))!.Role);
    }

    [Fact]
    public async Task An_admin_who_lost_the_role_can_no_longer_use_it()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Bystander");
        var former = await TestData.VolunteerAsync(db.DataSource, "Former Admin", VolunteerRole.Admin);
        await Volunteers.SetRoleAsync(former.Id, VolunteerRole.Member);

        Assert.Equal(MemberActionResult.NotAllowed, await Admin.DeleteAsync(former.Id, target.Id));
        Assert.False((await Volunteers.GetByIdAsync(target.Id))!.IsDeleted);
    }

    [Fact]
    public async Task An_admin_deletes_a_member_and_their_details_go()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Leaving Person");
        var admin = await TestData.VolunteerAsync(db.DataSource, "Admin", VolunteerRole.Admin);

        var result = await Admin.DeleteAsync(admin.Id, target.Id);

        Assert.Equal(MemberActionResult.Done, result);
        var gone = (await Volunteers.GetByIdAsync(target.Id))!;
        Assert.True(gone.IsDeleted);
        Assert.Null(gone.Email);
        Assert.DoesNotContain(await Volunteers.GetMembersAsync(), m => m.Id == target.Id);
    }

    [Fact]
    public async Task An_organizer_cannot_delete_anyone()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Staying Person");
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Organizer", VolunteerRole.Organizer);

        Assert.Equal(MemberActionResult.NotAllowed, await Admin.DeleteAsync(organizer.Id, target.Id));
        Assert.False((await Volunteers.GetByIdAsync(target.Id))!.IsDeleted);
    }

    [Fact]
    public async Task Nobody_deletes_an_admin_or_themselves_from_here()
    {
        var admin = await TestData.VolunteerAsync(db.DataSource, "Admin", VolunteerRole.Admin);
        var other = await TestData.VolunteerAsync(db.DataSource, "Other Admin", VolunteerRole.Admin);

        Assert.Equal(MemberActionResult.Self, await Admin.DeleteAsync(admin.Id, admin.Id));
        Assert.Equal(MemberActionResult.IsAdmin, await Admin.DeleteAsync(admin.Id, other.Id));
        Assert.False((await Volunteers.GetByIdAsync(other.Id))!.IsDeleted);
    }

    [Fact]
    public async Task Resending_queues_the_joining_email_once_however_often_it_is_pressed()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Waiting Person", confirmed: false);
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Organizer", VolunteerRole.Organizer);

        var first = await Admin.ResendConfirmationAsync(organizer.Id, target.Id);
        var second = await Admin.ResendConfirmationAsync(organizer.Id, target.Id);

        Assert.Equal(MemberActionResult.Done, first);
        Assert.Equal(MemberActionResult.RecentlySent, second);
        Assert.Equal(1, await ConfirmsQueuedAsync(target.Id));
    }

    [Fact]
    public async Task Nothing_is_resent_to_someone_confirmed_or_unsubscribed()
    {
        var organizer = await TestData.VolunteerAsync(db.DataSource, "Organizer", VolunteerRole.Organizer);
        var confirmed = await TestData.VolunteerAsync(db.DataSource, "Confirmed Person");
        var unsubscribed = await TestData.VolunteerAsync(db.DataSource, "Quiet Person", confirmed: false);
        await Volunteers.SetEmailOptInAsync(unsubscribed.Id, false);

        Assert.Equal(MemberActionResult.AlreadyConfirmed, await Admin.ResendConfirmationAsync(organizer.Id, confirmed.Id));
        Assert.Equal(MemberActionResult.CannotEmail, await Admin.ResendConfirmationAsync(organizer.Id, unsubscribed.Id));
        Assert.Equal(0, await ConfirmsQueuedAsync(confirmed.Id));
        Assert.Equal(0, await ConfirmsQueuedAsync(unsubscribed.Id));
    }

    [Fact]
    public async Task A_member_cannot_resend_anything()
    {
        var target = await TestData.VolunteerAsync(db.DataSource, "Waiting Person", confirmed: false);
        var member = await TestData.VolunteerAsync(db.DataSource, "Just A Member");

        Assert.Equal(MemberActionResult.NotAllowed, await Admin.ResendConfirmationAsync(member.Id, target.Id));
        Assert.Equal(0, await ConfirmsQueuedAsync(target.Id));
    }
}
