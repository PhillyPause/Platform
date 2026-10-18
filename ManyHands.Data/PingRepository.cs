using Dapper;
using Npgsql;

namespace ManyHands.Data;

/// <summary>The channels a volunteer can be told about an activity on, one claim each.</summary>
public static class PingChannel
{
    public const string Email = "email";
    public const string DirectMessage = "discord-dm";

    /// <summary>The note posted in an activity's Discord thread when someone signs up.</summary>
    public const string Thread = "discord-thread";
}

/// <summary>
/// "Has this person already been told about this activity, this way?" — the
/// record that makes the interest pings idempotent. The outbox redelivers, and
/// a redelivered publish must not mail anyone twice.
/// </summary>
public sealed class PingRepository(NpgsqlDataSource db)
{
    /// <summary>
    /// Takes the claim, or returns false if someone already has. Taken before
    /// sending, so a crash after the claim means a missed ping rather than a
    /// repeated one: the right way round for something unsolicited.
    /// </summary>
    public async Task<bool> TryClaimAsync(Guid activityId, Guid volunteerId, string channel, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO activity_pings (activity_id, volunteer_id, channel)
            VALUES (@activityId, @volunteerId, @channel)
            ON CONFLICT DO NOTHING
            """,
            new { activityId, volunteerId, channel }, cancellationToken: ct)) > 0;
    }

    public async Task MarkDeliveredAsync(
        Guid activityId, Guid volunteerId, string channel, bool delivered, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE activity_pings SET delivered = @delivered
            WHERE activity_id = @activityId AND volunteer_id = @volunteerId AND channel = @channel
            """,
            new { activityId, volunteerId, channel, delivered }, cancellationToken: ct));
    }
}
