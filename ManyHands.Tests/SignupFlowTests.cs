using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The critical path: a volunteer signs up from a public page and hears back.
/// The real repository against real Postgres, including capacity, idempotency,
/// and the outbox handoff that makes the confirmation email happen.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SignupFlowTests(DatabaseFixture db)
{
    private SignupRepository Signups => new(db.DataSource);
    private VolunteerRepository Volunteers => new(db.DataSource);
    private ActivityRepository Activities => new(db.DataSource);

    private Task<Guid> OneSeatAsync() =>
        TestData.ActivityAsync(db.DataSource, capacity: 1, tags: TestData.Flyering);

    [Fact]
    public async Task Signing_up_creates_the_volunteer_their_interests_and_the_signup()
    {
        var activity = await OneSeatAsync();
        var email = TestData.NewEmail();

        var result = await Signups.SignUpAsync(
            activity, "Maya Okafor", email, [TestData.Tabling, TestData.Flyering]);

        var ok = Assert.IsType<SignupResult.Ok>(result);
        Assert.Equal(SignupStatus.Confirmed, ok.Status);
        Assert.False(ok.AlreadySignedUp);

        var volunteer = await Volunteers.GetByEmailAsync(email);
        Assert.NotNull(volunteer);
        Assert.Equal("Maya Okafor", volunteer.DisplayName);
        Assert.Equal(VolunteerRole.Member, volunteer.Role);

        var interests = await Volunteers.GetInterestIdsAsync(ok.VolunteerId);
        Assert.Equal([TestData.Tabling, TestData.Flyering], interests.Order().ToArray());
    }

    [Fact]
    public async Task Signing_up_twice_queues_exactly_one_confirmation()
    {
        var activity = await OneSeatAsync();
        var email = TestData.NewEmail();

        var first = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Repeat Clicker", email, []));
        var again = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Repeat Clicker", email, []));

        Assert.True(again.AlreadySignedUp);
        Assert.Equal(first.SignupId, again.SignupId);
        Assert.Equal(1, await TestData.CountOutboxAsync(db.DataSource, OutboxKinds.SignupConfirmed, first.SignupId));
    }

    [Fact]
    public async Task Email_matching_is_case_insensitive()
    {
        var activity = await OneSeatAsync();
        var email = TestData.NewEmail();

        var a = Assert.IsType<SignupResult.Ok>(await Signups.SignUpAsync(activity, "Ada", email, []));
        var b = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Ada", email.ToUpperInvariant(), []));

        Assert.Equal(a.VolunteerId, b.VolunteerId);
    }

    [Fact]
    public async Task The_second_person_for_a_single_place_is_waitlisted()
    {
        var activity = await OneSeatAsync();

        var first = await Signups.SignUpAsync(activity, "First", TestData.NewEmail(), []);
        var second = await Signups.SignUpAsync(activity, "Second", TestData.NewEmail(), []);

        Assert.Equal(SignupStatus.Confirmed, Assert.IsType<SignupResult.Ok>(first).Status);
        Assert.Equal(SignupStatus.Waitlisted, Assert.IsType<SignupResult.Ok>(second).Status);
    }

    [Fact]
    public async Task Concurrent_signups_never_oversubscribe_a_capped_activity()
    {
        var activity = await OneSeatAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Signups.SignUpAsync(activity, "Racer", TestData.NewEmail(), [])));

        Assert.Equal(1, results.OfType<SignupResult.Ok>().Count(r => r.Status == SignupStatus.Confirmed));
    }

    [Fact]
    public async Task An_uncapped_activity_confirms_everybody()
    {
        var activity = await TestData.ActivityAsync(db.DataSource, "Big rally");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Signups.SignUpAsync(activity, "Crowd", TestData.NewEmail(), [])));

        Assert.All(results, r =>
            Assert.Equal(SignupStatus.Confirmed, Assert.IsType<SignupResult.Ok>(r).Status));
    }

    [Fact]
    public async Task An_address_that_is_not_an_email_is_refused()
    {
        var activity = await OneSeatAsync();

        Assert.IsType<SignupResult.InvalidEmail>(
            await Signups.SignUpAsync(activity, "Typo", "not-an-email", []));
    }

    [Fact]
    public async Task Nobody_signs_up_for_something_already_over()
    {
        var finished = await TestData.ActivityAsync(db.DataSource, "Yesterday", startsInHours: -26);

        Assert.IsType<SignupResult.ActivityFinished>(
            await Signups.SignUpAsync(finished, "Too Late", TestData.NewEmail(), []));
    }

    [Fact]
    public async Task Ongoing_work_with_no_time_is_never_finished()
    {
        var ongoing = await TestData.ActivityAsync(db.DataSource, "Ongoing", startsInHours: null);

        var result = await Signups.SignUpAsync(ongoing, "Whenever", TestData.NewEmail(), []);

        Assert.Equal(SignupStatus.Confirmed, Assert.IsType<SignupResult.Ok>(result).Status);
    }

    [Fact]
    public async Task A_draft_cannot_be_signed_up_for()
    {
        var draft = await TestData.ActivityAsync(db.DataSource, "Still a draft", published: false);

        Assert.IsType<SignupResult.ActivityNotFound>(
            await Signups.SignUpAsync(draft, "Early Bird", TestData.NewEmail(), []));
    }

    [Fact]
    public async Task Unknown_interest_ids_do_not_lose_the_signup()
    {
        // Unauthenticated input: a bogus tag is dropped rather than rolling the
        // signup back on a foreign-key violation.
        const short unknownType = 999;
        var activity = await OneSeatAsync();

        var ok = Assert.IsType<SignupResult.Ok>(await Signups.SignUpAsync(
            activity, "Tag Fuzzer", TestData.NewEmail(), [TestData.Flyering, unknownType]));

        var interests = await Volunteers.GetInterestIdsAsync(ok.VolunteerId);
        Assert.Equal([TestData.Flyering], interests.ToArray());
    }

    [Fact]
    public async Task Cancelling_then_signing_up_again_restores_the_signup_and_says_so()
    {
        var activity = await OneSeatAsync();
        var email = TestData.NewEmail();
        var first = Assert.IsType<SignupResult.Ok>(await Signups.SignUpAsync(activity, "Flaky", email, []));

        Assert.True(await Signups.CancelAsync(first.SignupId, first.VolunteerId));
        var again = Assert.IsType<SignupResult.Ok>(await Signups.SignUpAsync(activity, "Flaky", email, []));

        Assert.Equal(first.SignupId, again.SignupId);
        Assert.Equal(SignupStatus.Confirmed, again.Status);
        // A fresh commitment after a cancellation deserves the details again.
        Assert.Equal(2, await TestData.CountOutboxAsync(db.DataSource, OutboxKinds.SignupConfirmed, first.SignupId));
    }

    [Fact]
    public async Task Nobody_can_cancel_someone_elses_signup()
    {
        var activity = await TestData.ActivityAsync(db.DataSource);
        var victim = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Victim", TestData.NewEmail(), []));
        var attacker = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Attacker", TestData.NewEmail(), []));

        Assert.False(await Signups.CancelAsync(victim.SignupId, attacker.VolunteerId));
    }

    [Fact]
    public async Task The_public_list_carries_time_capacity_and_tags()
    {
        var activity = await OneSeatAsync();

        var item = Assert.Single(await Activities.GetPublishedAsync(), a => a.Id == activity);

        Assert.NotNull(item.StartsAt);
        Assert.NotNull(item.EndsAt);
        Assert.Equal(1, item.Capacity);
        Assert.Contains(TestData.FlyeringLabel, item.Tags);
    }

    [Fact]
    public async Task Finished_activities_and_drafts_stay_off_the_public_list()
    {
        var finished = await TestData.ActivityAsync(db.DataSource, "Already over", startsInHours: -3);
        var draft = await TestData.ActivityAsync(db.DataSource, "Draft", published: false);

        var published = await Activities.GetPublishedAsync();

        Assert.DoesNotContain(published, a => a.Id == finished);
        Assert.DoesNotContain(published, a => a.Id == draft);
        Assert.Null(await Activities.GetPublishedByIdAsync(draft));
    }

    [Fact]
    public async Task Filtering_by_type_returns_only_matching_activities()
    {
        var activity = await OneSeatAsync();

        Assert.Contains(await Activities.GetPublishedAsync(TestData.Flyering), a => a.Id == activity);
        Assert.DoesNotContain(await Activities.GetPublishedAsync(TestData.Calling), a => a.Id == activity);
    }

    [Fact]
    public async Task A_volunteer_sees_what_they_signed_up_for()
    {
        var activity = await OneSeatAsync();
        var ok = Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activity, "Lister", TestData.NewEmail(), []));

        var mine = Assert.Single(await Signups.GetForVolunteerAsync(ok.VolunteerId));

        Assert.Equal("Saturday flyering", mine.Title);
        Assert.Equal(SignupStatus.Confirmed, mine.Status);
    }
}
