using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// The critical path: a volunteer signs up from a public page and hears back.
/// One transaction creates or updates the volunteer, records the signup and
/// their interests, and queues the confirmation in the outbox. Delivery is the
/// outbox worker's job; this never talks to a mail provider.
/// </summary>
public sealed class SignupRepository(NpgsqlDataSource db)
{
    // Dapper maps columns onto constructor parameters by name; it does not map
    // value tuples, whose parameters are item1/item2. Hence real records.
    private sealed record ActivityRow(Guid Id, int? Capacity, bool Finished);
    private sealed record SignupRow(Guid Id, int Status, bool Inserted);

    public async Task<SignupResult> SignUpAsync(
        Guid activityId,
        string displayName,
        string email,
        IReadOnlyCollection<short> interestIds,
        SignupSource source = SignupSource.Web,
        string? note = null,
        CancellationToken ct = default)
    {
        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (!EmailAddress.LooksValid(normalizedEmail))
            return new SignupResult.InvalidEmail();

        var name = string.IsNullOrWhiteSpace(displayName)
            ? normalizedEmail.Split('@')[0]
            : displayName.Trim();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Serialises signups for this activity, so two simultaneous ones cannot
        // both read the same "one place left" and both take it. CancelAsync
        // takes the same lock, so a freed place goes to exactly one person.
        await LockActivityAsync(conn, tx, activityId, ct);

        var activity = await conn.QuerySingleOrDefaultAsync<ActivityRow>(new CommandDefinition(
            """
            SELECT id, capacity, ends_at IS NOT NULL AND ends_at <= now() AS finished
            FROM activities
            WHERE id = @activityId AND is_published
            """,
            new { activityId }, tx, cancellationToken: ct));

        if (activity is null)
            return new SignupResult.ActivityNotFound();

        // Signing up for something that already happened helps nobody, and the
        // public list has stopped showing it by now anyway.
        if (activity.Finished)
            return new SignupResult.ActivityFinished();

        var volunteerId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO volunteers (display_name, email)
            VALUES (@name, @normalizedEmail)
            ON CONFLICT (lower(email)) WHERE email IS NOT NULL
            DO UPDATE SET display_name = COALESCE(NULLIF(volunteers.display_name, ''), EXCLUDED.display_name)
            RETURNING id
            """,
            new { name, normalizedEmail }, tx, cancellationToken: ct));

        if (interestIds.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO volunteer_interests (volunteer_id, activity_type_id)
                -- Joining activity_types drops ids that do not exist. This is an
                -- unauthenticated form, and a foreign-key violation here would
                -- roll back the entire signup over a bad tag.
                SELECT @volunteerId, a.id
                FROM activity_types a
                WHERE a.id = ANY(@ids::smallint[]) AND a.is_active
                ON CONFLICT DO NOTHING
                """,
                new { volunteerId, ids = interestIds.ToArray() }, tx, cancellationToken: ct));
        }

        // Waitlist when the activity is already at capacity.
        var status = SignupStatus.Confirmed;
        if (activity.Capacity is int cap)
        {
            var taken = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT count(*)::int FROM signups
                WHERE activity_id = @activityId AND status = 0 AND volunteer_id <> @volunteerId
                """,
                new { activityId, volunteerId }, tx, cancellationToken: ct));

            if (taken >= cap) status = SignupStatus.Waitlisted;
        }

        // Re-signing up is not an error: return the existing row, and revive it
        // if the volunteer had cancelled.
        var signup = await conn.QuerySingleAsync<SignupRow>(new CommandDefinition(
            """
            WITH ins AS (
              INSERT INTO signups (activity_id, volunteer_id, status, source, note)
              VALUES (@activityId, @volunteerId, @status, @source, @note)
              ON CONFLICT DO NOTHING
              RETURNING id, status
            )
            SELECT id, status, true AS inserted FROM ins
            UNION ALL
            -- Only reached when the insert conflicted: `ins` is empty, so this
            -- returns the row that already existed.
            SELECT id, status, false AS inserted FROM signups
             WHERE NOT EXISTS (SELECT 1 FROM ins)
               AND volunteer_id = @volunteerId
               AND activity_id = @activityId
            LIMIT 1
            """,
            new { activityId, volunteerId, status = (int)status, source = (int)source, note },
            tx, cancellationToken: ct));

        var alreadySignedUp = !signup.Inserted;
        var revived = false;

        if (alreadySignedUp && (SignupStatus)signup.Status == SignupStatus.Cancelled)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE signups SET status = @status WHERE id = @id",
                new { status = (int)status, id = signup.Id }, tx, cancellationToken: ct));
            signup = signup with { Status = (int)status };
            revived = true;
        }

        // Announce new signups, and re-announce a cancelled one taken up again:
        // that is a fresh commitment and deserves the details. Re-submitting the
        // form for a signup that was already live is the case that stays quiet.
        if (!alreadySignedUp || revived)
        {
            await Outbox.QueueAsync(conn, tx, OutboxKinds.SignupConfirmed,
                new { signupId = signup.Id, volunteerId, activityId, status = status.ToString() }, ct);
        }

        await tx.CommitAsync(ct);

        return new SignupResult.Ok(signup.Id, volunteerId, (SignupStatus)signup.Status, alreadySignedUp);
    }

    /// <summary>Everything a volunteer has coming up and has been part of.</summary>
    public async Task<IReadOnlyList<MyActivity>> GetForVolunteerAsync(
        Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MyActivity>(new CommandDefinition(
            """
            SELECT g.id   AS signup_id,
                   a.id   AS activity_id,
                   a.title,
                   a.location,
                   a.starts_at,
                   a.ends_at,
                   g.status
            FROM signups g
            JOIN activities a ON a.id = g.activity_id
            WHERE g.volunteer_id = @volunteerId
            ORDER BY a.starts_at NULLS LAST, g.created_at DESC
            """,
            new { volunteerId }, cancellationToken: ct));
        return rows.AsList();
    }

    private sealed record CancelTarget(int Status, Guid ActivityId, int? Capacity, bool Finished);

    private sealed record PromotionCandidate(Guid Id, Guid VolunteerId);

    /// <summary>
    /// Cancels a signup, if it is this volunteer's. If it held a confirmed place
    /// on a capped activity, the earliest waitlisted volunteer takes the place
    /// in the same transaction, and their "you are in" email is queued: the
    /// promise the public page makes about the waitlist.
    /// </summary>
    public async Task<bool> CancelAsync(Guid signupId, Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var target = await conn.QuerySingleOrDefaultAsync<CancelTarget>(new CommandDefinition(
            """
            SELECT s.status,
                   s.activity_id,
                   a.capacity,
                   a.ends_at IS NOT NULL AND a.ends_at <= now() AS finished
            FROM signups s
            JOIN activities a ON a.id = s.activity_id
            WHERE s.id = @signupId AND s.volunteer_id = @volunteerId AND s.status <> 2
            """,
            new { signupId, volunteerId }, tx, cancellationToken: ct));

        if (target is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await LockActivityAsync(conn, tx, target.ActivityId, ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE signups SET status = 2 WHERE id = @signupId",
            new { signupId }, tx, cancellationToken: ct));

        // Promotion only makes sense when a confirmed place on a capped,
        // still-running activity was freed. Nobody gets promoted into the past.
        if (target.Status == (int)SignupStatus.Confirmed
            && target.Capacity is int capacity
            && !target.Finished)
        {
            var taken = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*)::int FROM signups WHERE activity_id = @a AND status = 0",
                new { a = target.ActivityId }, tx, cancellationToken: ct));

            if (taken < capacity)
            {
                var next = await conn.QuerySingleOrDefaultAsync<PromotionCandidate>(new CommandDefinition(
                    """
                    SELECT id, volunteer_id FROM signups
                    WHERE activity_id = @a AND status = 1
                    ORDER BY created_at
                    LIMIT 1
                    """,
                    new { a = target.ActivityId }, tx, cancellationToken: ct));

                if (next is not null)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE signups SET status = 0 WHERE id = @id",
                        new { next.Id }, tx, cancellationToken: ct));

                    await Outbox.QueueAsync(conn, tx, OutboxKinds.SignupPromoted,
                        new { signupId = next.Id }, ct);
                }
            }
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private static Task LockActivityAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@activityId::text, 0))",
            new { activityId }, tx, cancellationToken: ct));
}
