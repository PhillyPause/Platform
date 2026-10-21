using Dapper;
using ManyHands.Core;
using ManyHands.Data.Calendar;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// The reads behind the two ICS feeds. Both need an end time, so ongoing work
/// never appears: an activity with no times could only be an all-day event
/// repeating forever, a permanent smear across every subscriber's calendar.
/// "Call your representative" belongs on the activities page, not in a diary.
/// </summary>
public sealed class CalendarRepository(NpgsqlDataSource db)
{
    /// <summary>
    /// How far back a feed reaches. Subscribers expect a little history in the
    /// grid, and the bound keeps the feed from growing without limit.
    /// </summary>
    private static readonly TimeSpan Backfill = TimeSpan.FromDays(30);

    /// <summary>Every published, dated activity.</summary>
    public async Task<IReadOnlyList<CalendarEntry>> GetPublicFeedAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CalendarEntry>(new CommandDefinition(
            """
            SELECT a.id AS uid, a.title, a.description, a.location, a.meeting_url,
                   a.starts_at, a.ends_at, false AS is_tentative
            FROM activities a
            WHERE a.is_published
              AND a.ends_at IS NOT NULL
              AND a.ends_at > now() - @backfill
            ORDER BY a.starts_at
            """,
            new { backfill = Backfill }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// One volunteer's own commitments, or null when the key matches nobody so
    /// the endpoint can 404 rather than hand back a calendar that never fills.
    /// </summary>
    /// <param name="calendarKey">
    /// The credential. A calendar app has no session: whoever holds the key is
    /// treated as the volunteer, which is why it is a random uuid with its own
    /// unique index, separate from the unsubscribe key.
    /// </param>
    public async Task<IReadOnlyList<CalendarEntry>?> GetVolunteerFeedAsync(
        Guid calendarKey, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var volunteerId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM volunteers WHERE calendar_key = @calendarKey AND NOT is_deleted",
            new { calendarKey }, cancellationToken: ct));

        if (volunteerId is null) return null;

        var rows = await conn.QueryAsync<CalendarEntry>(new CommandDefinition(
            """
            SELECT g.id AS uid, a.title, a.description, a.location, a.meeting_url,
                   a.starts_at, a.ends_at, (g.status = @waitlisted) AS is_tentative
            FROM signups g
            JOIN activities a ON a.id = g.activity_id
            WHERE g.volunteer_id = @volunteerId
              -- A cancelled signup must disappear from the calendar it was
              -- added to, not linger as a commitment nobody is keeping.
              AND g.status <> @cancelled
              AND a.ends_at IS NOT NULL
              AND a.ends_at > now() - @backfill
            ORDER BY a.starts_at
            """,
            new
            {
                volunteerId,
                waitlisted = (int)SignupStatus.Waitlisted,
                cancelled = (int)SignupStatus.Cancelled,
                backfill = Backfill
            },
            cancellationToken: ct));
        return rows.AsList();
    }
}
