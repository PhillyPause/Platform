using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

public sealed record ParticipationRow(
    string DisplayName,
    string? Email,
    string Title,
    string Types,
    DateTimeOffset? StartsAt,
    SignupStatus Status,
    DateTimeOffset? CheckedInAt);

public sealed record TeamStats(
    int Volunteers,
    int UpcomingActivities,
    int ConfirmedSignups,
    int CheckedIn);

/// <summary>
/// What is waiting on the team, for the top of /team: who is new, who has not
/// confirmed their address, CRM tasks still open, and mail from strangers.
/// </summary>
public sealed record TeamOverview(
    int JoinedThisWeek,
    /// <summary>The members page's "Not confirmed": an address we may write to, never confirmed.</summary>
    int NotConfirmed,
    int OpenTasks,
    /// <summary>Open, with a due day before today on Philadelphia's clock.</summary>
    int OverdueTasks,
    /// <summary>Conversations with a message nobody has said who sent.</summary>
    int ThreadsToTriage);

/// <summary>
/// The numbers organizers get asked for. Participation, not hours: people
/// came to things, and that is what is counted.
/// </summary>
public sealed class ReportingRepository(NpgsqlDataSource db)
{
    public async Task<IReadOnlyList<ParticipationRow>> GetParticipationAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ParticipationRow>(new CommandDefinition(
            """
            SELECT v.display_name,
                   v.email,
                   a.title,
                   COALESCE((SELECT string_agg(t.label, '; ' ORDER BY t.sort_order)
                               FROM activity_tags at
                               JOIN activity_types t ON t.id = at.activity_type_id
                              WHERE at.activity_id = a.id), '') AS types,
                   a.starts_at,
                   g.status,
                   g.checked_in_at
            FROM signups g
            JOIN volunteers v ON v.id = g.volunteer_id
            JOIN activities a ON a.id = g.activity_id
            ORDER BY a.starts_at DESC NULLS LAST, v.display_name
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<TeamStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<TeamStats>(new CommandDefinition(
            """
            SELECT
              (SELECT count(*)::int FROM volunteers WHERE NOT is_deleted)                 AS volunteers,
              (SELECT count(*)::int FROM activities WHERE is_published AND starts_at > now()) AS upcoming_activities,
              (SELECT count(*)::int FROM signups WHERE status = 0)                        AS confirmed_signups,
              (SELECT count(*)::int FROM signups WHERE checked_in_at IS NOT NULL)         AS checked_in
            """,
            cancellationToken: ct));
    }

    /// <summary>Counts only, in one round trip: cheap enough for every visit to /team.</summary>
    public async Task<TeamOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<TeamOverview>(new CommandDefinition(
            """
            SELECT
              (SELECT count(*)::int FROM volunteers
                WHERE NOT is_deleted AND created_at > now() - interval '7 days')            AS joined_this_week,
              (SELECT count(*)::int FROM volunteers
                WHERE NOT is_deleted AND email IS NOT NULL AND email_verified_at IS NULL
                  AND email_opt_in AND unsubscribed_at IS NULL)                              AS not_confirmed,
              (SELECT count(*)::int FROM crm_tasks
                WHERE NOT is_deleted AND status = 0)                                         AS open_tasks,
              (SELECT count(*)::int FROM crm_tasks
                WHERE NOT is_deleted AND status = 0
                  AND due_on < (now() AT TIME ZONE @zone)::date)                              AS overdue_tasks,
              (SELECT count(DISTINCT conversation_id)::int FROM crm_emails WHERE needs_triage) AS threads_to_triage
            """,
            new { zone = Organisation.TimeZone }, cancellationToken: ct));
    }

    /// <summary>
    /// Everyone whose stated interests match any of these types: the audience
    /// for "we need callers on Thursday". Whether each may be emailed or
    /// messaged is decided per channel by the caller, from the same row.
    /// </summary>
    public async Task<IReadOnlyList<Volunteer>> GetInterestedAsync(
        IReadOnlyCollection<short> typeIds, CancellationToken ct = default)
    {
        if (typeIds.Count == 0) return [];

        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Volunteer>(new CommandDefinition(
            """
            SELECT v.id, v.display_name, v.email, v.discord_user_id, v.role, v.created_at,
                   v.email_verified_at, v.email_opt_in, v.unsubscribed_at, v.comms_key, v.calendar_key,
                   v.is_deleted
            FROM volunteers v
            WHERE NOT v.is_deleted
              AND EXISTS (SELECT 1 FROM volunteer_interests vi
                          WHERE vi.volunteer_id = v.id AND vi.activity_type_id = ANY(@ids::smallint[]))
            ORDER BY v.display_name
            """,
            new { ids = typeIds.ToArray() }, cancellationToken: ct));
        return rows.AsList();
    }
}
