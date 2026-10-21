using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The organizer's side: drafting, publishing (and the announcement that
/// goes with it exactly once), the roster, check-in, walk-ins and the door code.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrganizerTests(DatabaseFixture db)
{
    private ActivityAdminRepository Admin => new(db.DataSource);

    private static ActivityFields Fields(string title = "Leafleting at 30th Street", int? capacity = null, params short[] types) =>
        new(title, "Bring comfortable shoes.", ActivityKind.OneTime, "30th Street Station", null,
            DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), capacity, types);

    private async Task<int> PublishedAnnouncementsAsync(Guid activityId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM outbox WHERE kind = @kind AND payload->>'activityId' = @id",
            new { kind = OutboxKinds.ActivityPublished, id = activityId.ToString() });
    }

    [Fact]
    public async Task A_new_activity_is_a_draft_nobody_can_see()
    {
        var id = await Admin.CreateAsync(Fields(types: TestData.Flyering));

        Assert.Null(await new ActivityRepository(db.DataSource).GetPublishedByIdAsync(id));
        var draft = await Admin.GetAsync(id);
        Assert.False(draft!.IsPublished);
        Assert.Equal([TestData.Flyering], draft.TypeIds);
    }

    [Fact]
    public async Task Publishing_announces_once_however_often_it_is_pressed()
    {
        var id = await Admin.CreateAsync(Fields());

        Assert.True(await Admin.SetPublishedAsync(id, true));
        Assert.False(await Admin.SetPublishedAsync(id, true));
        Assert.True(await Admin.SetPublishedAsync(id, false));

        Assert.Equal(1, await PublishedAnnouncementsAsync(id));
        Assert.Null(await new ActivityRepository(db.DataSource).GetPublishedByIdAsync(id));
    }

    [Fact]
    public async Task Editing_replaces_the_types_and_keeps_the_rest()
    {
        var id = await Admin.CreateAsync(Fields(types: [TestData.Tabling, TestData.Flyering]));

        Assert.True(await Admin.UpdateAsync(id, Fields("Renamed", capacity: 3, TestData.Calling)));

        var draft = await Admin.GetAsync(id);
        Assert.Equal("Renamed", draft!.Title);
        Assert.Equal(3, draft.Capacity);
        Assert.Equal([TestData.Calling], draft.TypeIds);
    }

    [Fact]
    public async Task The_roster_lists_signups_and_check_in_flips_both_ways()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var ok = Assert.IsType<SignupResult.Ok>(
            await new SignupRepository(db.DataSource).SignUpAsync(activity, "Roster Person", TestData.NewEmail(), []));

        Assert.True(await Admin.SetCheckedInAsync(ok.SignupId, true));
        Assert.NotNull(Assert.Single(await Admin.GetRosterAsync(activity)).CheckedInAt);

        Assert.True(await Admin.SetCheckedInAsync(ok.SignupId, false));
        Assert.Null(Assert.Single(await Admin.GetRosterAsync(activity)).CheckedInAt);
    }

    [Fact]
    public async Task A_walk_in_is_counted_even_past_capacity_and_with_no_email()
    {
        var activity = await TestData.ActivityAsync(db.DataSource, capacity: 1);
        await new SignupRepository(db.DataSource).SignUpAsync(activity, "Booked", TestData.NewEmail(), []);

        Assert.True(await Admin.AddWalkInAsync(activity, "Just Turned Up", email: null));

        var roster = await Admin.GetRosterAsync(activity);
        var walkIn = Assert.Single(roster, r => r.DisplayName == "Just Turned Up");
        Assert.Equal(SignupStatus.Confirmed, walkIn.Status);
        Assert.Equal(SignupSource.Organizer, walkIn.Source);
        Assert.NotNull(walkIn.CheckedInAt);
        Assert.Equal(2, roster.Count(r => r.Status == SignupStatus.Confirmed));
    }

    [Fact]
    public async Task A_walk_in_with_a_bad_address_is_refused()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);

        Assert.False(await Admin.AddWalkInAsync(activity, "Typo", "not-an-email"));
    }

    [Fact]
    public async Task Checking_yourself_in_matches_the_address_you_signed_up_with()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var email = TestData.NewEmail();
        await new SignupRepository(db.DataSource).SignUpAsync(activity, "Self Checker", email, []);
        var checkIn = new SelfCheckInRepository(db.DataSource);

        var first = Assert.IsType<SelfCheckInResult.Ok>(await checkIn.CheckInAsync(activity, email.ToUpperInvariant()));
        var again = Assert.IsType<SelfCheckInResult.Ok>(await checkIn.CheckInAsync(activity, email));

        Assert.False(first.AlreadyCheckedIn);
        Assert.True(again.AlreadyCheckedIn);
        Assert.IsType<SelfCheckInResult.NotSignedUp>(await checkIn.CheckInAsync(activity, TestData.NewEmail()));
    }

    [Fact]
    public async Task Publishing_emails_the_interested_once_and_nobody_else()
    {
        var keen = await TestData.VolunteerAsync(db.DataSource, "Keen", interests: TestData.Flyering);
        var other = await TestData.VolunteerAsync(db.DataSource, "Other", interests: TestData.Calling);
        var quiet = await TestData.VolunteerAsync(db.DataSource, "Quiet", interests: TestData.Flyering);
        await new VolunteerRepository(db.DataSource).SetEmailOptInAsync(quiet.Id, false);
        var unsure = await TestData.VolunteerAsync(db.DataSource, "Unsure", confirmed: false, interests: TestData.Flyering);

        var id = await Admin.CreateAsync(Fields($"Flyering {Guid.NewGuid():N}", types: TestData.Flyering));
        await Admin.SetPublishedAsync(id, true);
        var dispatcher = TestData.Dispatcher(db.DataSource);
        var message = TestData.Message(OutboxKinds.ActivityPublished, new { activityId = id });

        // Twice: the outbox redelivers, and a redelivery must mail nobody again.
        await dispatcher.DispatchAsync(message, CancellationToken.None);
        await dispatcher.DispatchAsync(message, CancellationToken.None);

        Assert.Single(await TestData.SubjectsSentToAsync(db.DataSource, keen.Email!));
        Assert.Empty(await TestData.SubjectsSentToAsync(db.DataSource, other.Email!));
        Assert.Empty(await TestData.SubjectsSentToAsync(db.DataSource, quiet.Email!));
        Assert.Empty(await TestData.SubjectsSentToAsync(db.DataSource, unsure.Email!));
    }
}
