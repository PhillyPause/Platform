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
