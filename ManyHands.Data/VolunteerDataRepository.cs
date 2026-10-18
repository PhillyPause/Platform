using Dapper;
using Npgsql;

namespace ManyHands.Data;

/// <summary>Everything ManyHands holds about one person, for export.</summary>
public sealed record VolunteerExport(
    Guid VolunteerId,
    string DisplayName,
    string? Email,
    string Role,
    DateTimeOffset JoinedAt,
    bool EmailOptIn,
    DateTimeOffset? UnsubscribedAt,
    IReadOnlyList<string> Interests,
    IReadOnlyList<ExportedSignup> Signups,
    IReadOnlyList<ExportedMessage> EmailsReceived);

public sealed record ExportedSignup(
    string Activity,
    DateTimeOffset? StartsAt,
    string Status,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset SignedUpAt);

public sealed record ExportedMessage(string Subject, string Kind, DateTimeOffset SentAt);

/// <summary>
/// A volunteer's copy of their own data, and their deletion. Built in from the
/// start because it is painful to retrofit and it is the right thing to offer.
/// </summary>
public sealed class VolunteerDataRepository(NpgsqlDataSource db)
{
    private sealed record ExportHeader(
        Guid Id, string DisplayName, string? Email, int Role, DateTimeOffset CreatedAt,
        bool EmailOptIn, DateTimeOffset? UnsubscribedAt);

    public async Task<VolunteerExport?> ExportAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var volunteer = await conn.QuerySingleOrDefaultAsync<ExportHeader>(new CommandDefinition(
            """
            SELECT id, display_name, email, role, created_at, email_opt_in, unsubscribed_at
            FROM volunteers WHERE id = @volunteerId AND NOT is_deleted
            """,
            new { volunteerId }, cancellationToken: ct));

        if (volunteer is null) return null;

        var interests = (await conn.QueryAsync<string>(new CommandDefinition(
            """
            SELECT a.label FROM volunteer_interests vi
            JOIN activity_types a ON a.id = vi.activity_type_id
            WHERE vi.volunteer_id = @volunteerId
            ORDER BY a.sort_order
            """,
            new { volunteerId }, cancellationToken: ct))).AsList();

        var signups = (await conn.QueryAsync<ExportedSignup>(new CommandDefinition(
            """
            SELECT a.title AS activity,
                   a.starts_at,
                   CASE g.status WHEN 0 THEN 'Confirmed' WHEN 1 THEN 'Waitlisted' ELSE 'Cancelled' END AS status,
                   g.checked_in_at,
                   g.created_at AS signed_up_at
            FROM signups g
            JOIN activities a ON a.id = g.activity_id
            WHERE g.volunteer_id = @volunteerId
            ORDER BY g.created_at
            """,
            new { volunteerId }, cancellationToken: ct))).AsList();

        // Subjects and times only. The bodies are what the person already
        // received, and would bloat the export without telling them anything.
        var messages = (await conn.QueryAsync<ExportedMessage>(new CommandDefinition(
            """
            SELECT subject, kind, COALESCE(sent_at, created_at) AS sent_at
            FROM email_messages
            WHERE volunteer_id = @volunteerId
            ORDER BY created_at
            """,
            new { volunteerId }, cancellationToken: ct))).AsList();

        return new VolunteerExport(
            volunteer.Id, volunteer.DisplayName, volunteer.Email,
            ((Core.VolunteerRole)volunteer.Role).ToString(), volunteer.CreatedAt,
            volunteer.EmailOptIn, volunteer.UnsubscribedAt,
            interests, signups, messages);
    }

    /// <summary>
    /// Erases the person while leaving the record honest: signups become
    /// anonymous rather than vanishing, so "twelve people came" stays true.
    /// Identifying columns are cleared; credentials, interests and Discord
    /// grants are destroyed outright.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE volunteers
            SET display_name      = 'Deleted volunteer',
                email             = NULL,
                discord_user_id   = NULL,
                role              = 0,
                email_opt_in      = false,
                unsubscribed_at   = COALESCE(unsubscribed_at, now()),
                email_verified_at = NULL,
                -- New keys, so any unsubscribe or calendar link already in the
                -- wild stops resolving to this row.
                comms_key         = gen_random_uuid(),
                calendar_key      = gen_random_uuid(),
                is_deleted        = true,
                deleted_at        = now()
            WHERE id = @volunteerId AND NOT is_deleted
            """,
            new { volunteerId }, tx, cancellationToken: ct));

        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        foreach (var table in new[] { "auth_tokens", "volunteer_interests", "discord_role_grants" })
        {
            await conn.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {table} WHERE volunteer_id = @volunteerId",
                new { volunteerId }, tx, cancellationToken: ct));
        }

        // Mail we sent them: keep the fact and the timing for deliverability
        // questions, drop the address and the words.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE email_messages
            SET to_address = 'deleted@invalid', body_text = '(deleted)', subject = '(deleted)'
            WHERE volunteer_id = @volunteerId
            """,
            new { volunteerId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return true;
    }
}
