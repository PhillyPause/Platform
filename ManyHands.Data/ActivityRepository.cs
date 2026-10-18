using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>What the public pages show: published activities, and one at a time.</summary>
public sealed class ActivityRepository(NpgsqlDataSource db)
{
    // Dapper matches constructor parameters by exact type, so the array column
    // must be read as string[]; ActivityListItem exposes it as
    // IReadOnlyList<string>, which Dapper would reject. Hence this shape, then
    // a projection. Internal so the materialisation tests can guard it.
    internal sealed record ListRow(
        Guid Id,
        string Title,
        string? Description,
        ActivityKind Kind,
        string? Location,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        string? MeetingUrl,
        int SignupCount,
        int? Capacity,
        string[] Tags);

    private const string ListSelect =
        """
        SELECT a.id,
               a.title,
               a.description,
               a.kind,
               a.location,
               a.starts_at,
               a.ends_at,
               a.meeting_url,
               (SELECT count(*)::int FROM signups g
                 WHERE g.activity_id = a.id AND g.status = 0)                AS signup_count,
               a.capacity,
               COALESCE((SELECT array_agg(t.label ORDER BY t.sort_order)
                           FROM activity_tags at
                           JOIN activity_types t ON t.id = at.activity_type_id
                          WHERE at.activity_id = a.id), '{}'::text[])        AS tags
        FROM activities a
        """;

    private static ActivityListItem ToItem(ListRow r) => new(
        r.Id, r.Title, r.Description, r.Kind, r.Location,
        r.StartsAt, r.EndsAt, r.MeetingUrl, r.SignupCount, r.Capacity, r.Tags);

    /// <summary>
    /// Published activities that have not finished. Soonest first; undated
    /// ongoing work sorts last but stays on the page.
    /// </summary>
    public async Task<IReadOnlyList<ActivityListItem>> GetPublishedAsync(
        short? activityTypeId = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ListRow>(new CommandDefinition(
            $"""
             {ListSelect}
             WHERE a.is_published
               AND (a.ends_at IS NULL OR a.ends_at > now())
               AND (@activityTypeId::smallint IS NULL OR EXISTS (
                     SELECT 1 FROM activity_tags at
                      WHERE at.activity_id = a.id
                        AND at.activity_type_id = @activityTypeId))
             ORDER BY a.starts_at NULLS LAST, a.created_at DESC
             """,
            new { activityTypeId },
            cancellationToken: ct));
        return rows.Select(ToItem).ToList();
    }

    /// <summary>One published activity, or null for a draft or an unknown id.</summary>
    public async Task<ActivityListItem?> GetPublishedByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ListRow>(new CommandDefinition(
            $"""
             {ListSelect}
             WHERE a.id = @id AND a.is_published
             """,
            new { id },
            cancellationToken: ct));
        return row is null ? null : ToItem(row);
    }
}
