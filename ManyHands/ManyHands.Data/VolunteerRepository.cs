using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

public sealed class VolunteerRepository(NpgsqlDataSource db)
{
    internal const string Columns =
        """
        id, display_name, email, discord_user_id, role, created_at,
        email_verified_at, email_opt_in, unsubscribed_at, comms_key, calendar_key,
        is_deleted
        """;

    public async Task<Volunteer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Volunteer>(new CommandDefinition(
            $"SELECT {Columns} FROM volunteers WHERE id = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task<Volunteer?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Volunteer>(new CommandDefinition(
            $"SELECT {Columns} FROM volunteers WHERE lower(email) = lower(@email)",
            new { email = email.Trim() }, cancellationToken: ct));
    }

    /// <summary>Find-or-create by email. The sign-in path: asking for a link is joining.</summary>
    public async Task<Guid> UpsertByEmailAsync(
        string email, string? displayName, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? email.Trim().Split('@')[0]
            : displayName.Trim();

        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO volunteers (display_name, email)
            VALUES (@name, @email)
            ON CONFLICT (lower(email)) WHERE email IS NOT NULL
            DO UPDATE SET display_name = COALESCE(NULLIF(volunteers.display_name, ''), EXCLUDED.display_name)
            RETURNING id
            """,
            new { name, email = email.Trim() }, cancellationToken: ct));
    }

    private sealed record JoinedRow(Guid Id, bool Inserted, bool Confirmed);

    /// <summary>
    /// How long the one reminder waits for someone who has not clicked their
    /// confirmation link: long enough to be past a busy week's inbox, soon
    /// enough that they still remember joining.
    /// </summary>
    public static readonly TimeSpan NudgeAfter = TimeSpan.FromDays(3);

    /// <summary>
    /// Joining PhillyPause from the join page: the person, what they like doing,
    /// and an email asking them to confirm the address is theirs, queued in the
    /// same transaction so a failed join mails nobody. Anyone with the URL may
    /// join, the same trust as the public activity list they could already
    /// sign up from; the confirmation is what keeps a stranger's typo from
    /// putting someone else on the list.
    /// </summary>
    public async Task<JoinResult> JoinAsync(
        string displayName, string email, IReadOnlyCollection<short> interestIds,
        CancellationToken ct = default)
    {
        var address = email?.Trim() ?? string.Empty;
        if (!EmailAddress.LooksValid(address)) return new JoinResult.InvalidEmail();

        var name = string.IsNullOrWhiteSpace(displayName) ? address.Split('@')[0] : displayName.Trim();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var joined = await JoinCoreAsync(conn, tx, name, address, interestIds, addressProven: false, ct);

        if (!joined.Confirmed)
        {
            // Joining again before confirming is how someone says the first
            // email never came, so it goes again. The reminder is booked once,
            // with the first join.
            await Outbox.QueueAsync(conn, tx, OutboxKinds.MemberConfirm, new { volunteerId = joined.VolunteerId }, ct);
            if (!joined.AlreadyMember)
                await Outbox.QueueAsync(conn, tx, OutboxKinds.MemberNudge, new { volunteerId = joined.VolunteerId }, ct,
                    delay: NudgeAfter);
        }

        await tx.CommitAsync(ct);
        return joined;
    }

    /// <summary>
    /// What both doors share — the join page and an accepted invite: upsert the
    /// person and record what they like doing. An invite proves the address,
    /// since it was mailed there, so it confirms the person and welcomes them
    /// the first time; the join page leaves the asking to its caller.
    /// </summary>
    internal static async Task<JoinResult.Ok> JoinCoreAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name, string address,
        IReadOnlyCollection<short> interestIds, bool addressProven, CancellationToken ct)
    {
        // xmax is zero only on a row this statement inserted, which is how the
        // upsert says whether the person is new.
        var joined = await conn.QuerySingleAsync<JoinedRow>(new CommandDefinition(
            """
            INSERT INTO volunteers (display_name, email, email_verified_at)
            VALUES (@name, @address, CASE WHEN @addressProven THEN now() END)
            ON CONFLICT (lower(email)) WHERE email IS NOT NULL
            DO UPDATE SET display_name = COALESCE(NULLIF(volunteers.display_name, ''), EXCLUDED.display_name),
                          email_verified_at = COALESCE(volunteers.email_verified_at, EXCLUDED.email_verified_at)
            RETURNING id, (xmax = 0) AS inserted, email_verified_at IS NOT NULL AS confirmed
            """,
            new { name, address, addressProven }, tx, cancellationToken: ct));

        if (interestIds.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO volunteer_interests (volunteer_id, activity_type_id)
                SELECT @id, a.id FROM activity_types a
                WHERE a.id = ANY(@ids::smallint[]) AND a.is_active
                ON CONFLICT DO NOTHING
                """,
                new { joined.Id, ids = interestIds.ToArray() }, tx, cancellationToken: ct));
        }

        if (joined.Inserted && addressProven)
            await Outbox.QueueAsync(conn, tx, OutboxKinds.MemberWelcome, new { volunteerId = joined.Id }, ct);

        return new JoinResult.Ok(joined.Id, AlreadyMember: !joined.Inserted, joined.Confirmed);
    }

    /// <summary>Raises a role, never lowers it: how a configured admin becomes one on signing in.</summary>
    public async Task PromoteAsync(Guid volunteerId, VolunteerRole role, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET role = GREATEST(role, @role) WHERE id = @volunteerId AND NOT is_deleted",
            new { volunteerId, role = (int)role }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<short>> GetInterestIdsAsync(
        Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<short>(new CommandDefinition(
            "SELECT activity_type_id FROM volunteer_interests WHERE volunteer_id = @volunteerId",
            new { volunteerId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Replaces the volunteer's interests with exactly this set.</summary>
    public async Task SetInterestsAsync(
        Guid volunteerId, IReadOnlyCollection<short> interestIds, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM volunteer_interests
            WHERE volunteer_id = @volunteerId AND activity_type_id <> ALL(@ids::smallint[])
            """,
            new { volunteerId, ids = interestIds.ToArray() }, tx, cancellationToken: ct));

        if (interestIds.Count > 0)
        {
            // Joining activity_types drops ids that do not exist, so a tampered
            // form cannot fail the whole save on a foreign key.
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO volunteer_interests (volunteer_id, activity_type_id)
                SELECT @volunteerId, a.id FROM activity_types a
                WHERE a.id = ANY(@ids::smallint[]) AND a.is_active
                ON CONFLICT DO NOTHING
                """,
                new { volunteerId, ids = interestIds.ToArray() }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The volunteer's own switch for mail. Turning it back on clears an
    /// earlier unsubscribe: they are asking for mail again, in person.
    /// </summary>
    public async Task SetEmailOptInAsync(Guid volunteerId, bool optIn, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE volunteers
            SET email_opt_in = @optIn,
                unsubscribed_at = CASE WHEN @optIn THEN NULL ELSE COALESCE(unsubscribed_at, now()) END
            WHERE id = @volunteerId AND NOT is_deleted
            """,
            new { volunteerId, optIn }, cancellationToken: ct));
    }

    /// <summary>Everyone, with what they like doing: the members page.</summary>
    public async Task<IReadOnlyList<Member>> GetMembersAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Member>(new CommandDefinition(
            """
            SELECT v.id, v.display_name, v.email, v.role, v.created_at,
                   (v.email_opt_in AND v.unsubscribed_at IS NULL) AS can_email,
                   v.email_verified_at IS NOT NULL AS confirmed,
                   COALESCE((SELECT array_agg(a.label ORDER BY a.sort_order)
                               FROM volunteer_interests vi
                               JOIN activity_types a ON a.id = vi.activity_type_id
                              WHERE vi.volunteer_id = v.id), '{}'::text[]) AS interests
            FROM volunteers v
            WHERE NOT v.is_deleted
            ORDER BY v.role DESC, lower(v.display_name)
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> SetRoleAsync(Guid volunteerId, VolunteerRole role, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET role = @role WHERE id = @volunteerId AND NOT is_deleted",
            new { volunteerId, role = (int)role }, cancellationToken: ct)) > 0;
    }

    /// <summary>How many admins are left, so the last one cannot demote themselves.</summary>
    public async Task<int> CountAdminsAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM volunteers WHERE role = 2 AND NOT is_deleted",
            cancellationToken: ct));
    }

    /// <summary>One-click unsubscribe by opaque key — no sign-in required.</summary>
    public async Task<bool> UnsubscribeByCommsKeyAsync(Guid commsKey, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE volunteers
            SET unsubscribed_at = now(), email_opt_in = false
            WHERE comms_key = @commsKey AND unsubscribed_at IS NULL
            """,
            new { commsKey }, cancellationToken: ct));
        return rows > 0;
    }
}
