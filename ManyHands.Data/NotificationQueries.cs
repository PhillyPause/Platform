using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>Everything needed to compose one signup's message, in one round trip.</summary>
public sealed record SignupNotification(
    Guid SignupId,
    Guid VolunteerId,
    string DisplayName,
    string? Email,
    bool EmailOptIn,
    DateTimeOffset? UnsubscribedAt,
    Guid CommsKey,
    ulong? DiscordUserId,
    bool IsDeleted,
    Guid ActivityId,
    string Title,
    string? Location,
    string? MeetingUrl,
    DateTimeOffset? StartsAt,
    SignupStatus Status)
{
    /// <summary>
    /// The fields that decide whether we may email. It carries no calendar key
    /// or role: anything that needs those reads the volunteer row.
    /// </summary>
    public Volunteer AsVolunteer() => new(
        VolunteerId, DisplayName, Email, DiscordUserId, VolunteerRole.Member, default,
        EmailVerifiedAt: null, EmailOptIn, UnsubscribedAt, CommsKey,
        CalendarKey: default, IsDeleted);
}

/// <summary>The read models the outbox handlers and the reminder sweep need.</summary>
public sealed class NotificationQueries(NpgsqlDataSource db)
{
    private const string SignupSelect =
        """
        SELECT g.id            AS signup_id,
               v.id            AS volunteer_id,
               v.display_name,
               v.email,
               v.email_opt_in,
               v.unsubscribed_at,
               v.comms_key,
               v.discord_user_id,
               v.is_deleted,
               a.id            AS activity_id,
               a.title,
               a.location,
               a.meeting_url,
               a.starts_at,
               g.status
        FROM signups g
        JOIN volunteers v ON v.id = g.volunteer_id
        JOIN activities a ON a.id = g.activity_id
        """;

    public async Task<SignupNotification?> GetSignupAsync(Guid signupId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SignupNotification>(new CommandDefinition(
            $"{SignupSelect} WHERE g.id = @signupId",
            new { signupId }, cancellationToken: ct));
    }

    /// <summary>
    /// Confirmed signups for activities starting inside the window that have not
    /// been reminded yet. The outbox row itself is the record: a reminder
    /// already queued for this signup is not queued again.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetDueRemindersAsync(TimeSpan lookahead, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT g.id
            FROM signups g
            JOIN activities a ON a.id = g.activity_id
            WHERE g.status = 0
              AND a.is_published
              AND a.starts_at IS NOT NULL
              AND a.starts_at > now()
              AND a.starts_at <= now() + @lookahead
              AND NOT EXISTS (
                    SELECT 1 FROM outbox ob
                    WHERE ob.kind = @kind
                      AND ob.payload->>'signupId' = g.id::text)
            """,
            new { lookahead, kind = OutboxKinds.ActivityReminder }, cancellationToken: ct));
        return rows.AsList();
    }
}
