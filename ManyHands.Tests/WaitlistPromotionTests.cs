using Dapper;
using ManyHands.Core;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The public page says "sign up anyway and we will email you if a place
/// opens". These are that sentence, enforced: cancelling a confirmed place
/// promotes the earliest waitlisted volunteer and queues their email.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WaitlistPromotionTests(DatabaseFixture db)
{
    private SignupRepository Signups => new(db.DataSource);

    private Task<Guid> OneSeatAsync() => TestData.ActivityAsync(db.DataSource, "One seat", capacity: 1);

    private async Task<SignupResult.Ok> SignUpAsync(Guid activityId, string name) =>
        Assert.IsType<SignupResult.Ok>(
            await Signups.SignUpAsync(activityId, name, TestData.NewEmail(), []));

    private async Task<SignupStatus> StatusOfAsync(Guid signupId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return (SignupStatus)await conn.ExecuteScalarAsync<int>(
            "SELECT status FROM signups WHERE id = @signupId", new { signupId });
    }

    private Task<int> PromotionsQueuedForAsync(Guid signupId) =>
        TestData.CountOutboxAsync(db.DataSource, OutboxKinds.SignupPromoted, signupId);

    [Fact]
    public async Task Cancelling_a_confirmed_place_promotes_the_earliest_waitlisted()
    {
        var activity = await OneSeatAsync();
        var holder = await SignUpAsync(activity, "Holder");
        var first = await SignUpAsync(activity, "First In Line");
        var second = await SignUpAsync(activity, "Second In Line");

        Assert.True(await Signups.CancelAsync(holder.SignupId, holder.VolunteerId));

        Assert.Equal(SignupStatus.Confirmed, await StatusOfAsync(first.SignupId));
        Assert.Equal(SignupStatus.Waitlisted, await StatusOfAsync(second.SignupId));
        Assert.Equal(1, await PromotionsQueuedForAsync(first.SignupId));
        Assert.Equal(0, await PromotionsQueuedForAsync(second.SignupId));
    }

    [Fact]
    public async Task Cancelling_a_waitlisted_signup_promotes_nobody()
    {
        var activity = await OneSeatAsync();
        var holder = await SignUpAsync(activity, "Holder");
        var quitter = await SignUpAsync(activity, "Waitlisted Quitter");
        var stays = await SignUpAsync(activity, "Stays Waitlisted");

        Assert.True(await Signups.CancelAsync(quitter.SignupId, quitter.VolunteerId));

        Assert.Equal(SignupStatus.Confirmed, await StatusOfAsync(holder.SignupId));
        Assert.Equal(SignupStatus.Waitlisted, await StatusOfAsync(stays.SignupId));
        Assert.Equal(0, await PromotionsQueuedForAsync(stays.SignupId));
    }

    [Fact]
    public async Task Nobody_is_promoted_into_an_activity_that_already_finished()
    {
        var activity = await OneSeatAsync();
        var holder = await SignUpAsync(activity, "Holder");
        var waitlisted = await SignUpAsync(activity, "Hopeful");

        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "UPDATE activities SET starts_at = now() - interval '3 hours', ends_at = now() - interval '1 hour' WHERE id = @activity",
                new { activity });
        }

        Assert.True(await Signups.CancelAsync(holder.SignupId, holder.VolunteerId));

        Assert.Equal(SignupStatus.Waitlisted, await StatusOfAsync(waitlisted.SignupId));
        Assert.Equal(0, await PromotionsQueuedForAsync(waitlisted.SignupId));
    }

    [Fact]
    public async Task A_cancellation_racing_ten_signups_still_fills_exactly_one_place()
    {
        var activity = await OneSeatAsync();
        var holder = await SignUpAsync(activity, "Holder");
        await SignUpAsync(activity, "Hopeful");

        // The freed place is contested by the promotion and ten new signups.
        var cancel = Signups.CancelAsync(holder.SignupId, holder.VolunteerId);
        var rush = Enumerable.Range(0, 10).Select(_ =>
            Signups.SignUpAsync(activity, "Racer", TestData.NewEmail(), []));
        await Task.WhenAll(rush.Cast<Task>().Append(cancel));

        await using var conn = await db.DataSource.OpenConnectionAsync();
        var confirmed = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM signups WHERE activity_id = @activity AND status = 0",
            new { activity });

        Assert.Equal(1, confirmed);
    }
}
