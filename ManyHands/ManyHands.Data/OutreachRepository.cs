using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// The outreach log: people contacting their elected officials from the
/// action centre. A member sees their own; organizers see everyone's. It holds
/// that an action happened, how and to whom, never the words that were sent.
/// </summary>
public sealed class OutreachRepository(NpgsqlDataSource db)
{
    /// <summary>The longest note kept, in characters. The column checks it too.</summary>
    public const int NoteLimit = 500;

    private const string Select =
        """
        SELECT o.id, o.volunteer_id,
               v.display_name AS volunteer_name,
               o.official_key, o.official_name, o.channel, o.outcome, o.note, o.occurred_at
        FROM outreach_actions o
        LEFT JOIN volunteers v ON v.id = o.volunteer_id
        """;

    /// <summary>Records one action. <paramref name="volunteerId"/> is null for someone not signed in.</summary>
    public async Task<Guid> LogAsync(
        Guid? volunteerId,
        string officialKey,
        string officialName,
        OutreachChannel channel,
        OutreachOutcome? outcome,
        string? note,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officialKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(officialName);

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > NoteLimit }) note = note[..NoteLimit];

        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO outreach_actions (volunteer_id, official_key, official_name, channel, outcome, note)
            VALUES (@volunteerId, @officialKey, @officialName, @channel, @outcome, @note)
            RETURNING id
            """,
            new { volunteerId, officialKey, officialName, channel = (int)channel, outcome = (int?)outcome, note },
            cancellationToken: ct));
    }

    /// <summary>One person's own actions, newest first.</summary>
    public async Task<IReadOnlyList<OutreachAction>> ForVolunteerAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE o.volunteer_id = @volunteerId ORDER BY o.occurred_at DESC LIMIT 500",
            new { volunteerId }, cancellationToken: ct));
        return rows.Select(r => r.ToAction()).ToArray();
    }

    /// <summary>Everyone's, newest first.</summary>
    public async Task<IReadOnlyList<OutreachAction>> RecentAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            Select + " ORDER BY o.occurred_at DESC LIMIT @limit",
            new { limit = Math.Clamp(limit, 1, 10_000) }, cancellationToken: ct));
        return rows.Select(r => r.ToAction()).ToArray();
    }

    public async Task<OutreachTotals> TotalsAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<OutreachTotals>(new CommandDefinition(
            """
            SELECT
              count(*) FILTER (WHERE occurred_at > now() - interval '7 days')::int  AS this_week,
              count(*) FILTER (WHERE occurred_at > now() - interval '30 days')::int AS last30_days,
              count(*)::int                                                          AS all_time,
              count(DISTINCT volunteer_id) FILTER (WHERE occurred_at > now() - interval '30 days')::int AS people_last30_days,
              count(DISTINCT official_key) FILTER (WHERE occurred_at > now() - interval '30 days')::int AS officials_last30_days,
              count(*) FILTER (WHERE volunteer_id IS NULL AND occurred_at > now() - interval '30 days')::int AS anonymous_last30_days
            FROM outreach_actions
            """,
            cancellationToken: ct));
    }

    /// <summary>Every official anyone has contacted, most contacted first.</summary>
    public async Task<IReadOnlyList<OutreachByOfficial>> ByOfficialAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutreachByOfficial>(new CommandDefinition(
            """
            SELECT official_key,
                   (array_agg(official_name ORDER BY occurred_at DESC))[1] AS official_name,
                   count(*)::int                                  AS actions,
                   count(*) FILTER (WHERE channel = 1)::int       AS calls,
                   count(*) FILTER (WHERE channel IN (0, 2))::int AS emails,
                   count(DISTINCT volunteer_id)::int              AS people,
                   max(occurred_at)                               AS last_at
            FROM outreach_actions
            GROUP BY official_key
            ORDER BY actions DESC, last_at DESC
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>The people doing it, most active in the last 90 days first.</summary>
    public async Task<IReadOnlyList<OutreachByVolunteer>> ByVolunteerAsync(int limit = 25, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutreachByVolunteer>(new CommandDefinition(
            """
            SELECT v.id AS volunteer_id, v.display_name AS volunteer_name,
                   count(*)::int AS actions,
                   count(DISTINCT o.official_key)::int AS officials,
                   max(o.occurred_at) AS last_at
            FROM outreach_actions o
            JOIN volunteers v ON v.id = o.volunteer_id AND NOT v.is_deleted
            WHERE o.occurred_at > now() - interval '90 days'
            GROUP BY v.id, v.display_name
            ORDER BY actions DESC, last_at DESC
            LIMIT @limit
            """,
            new { limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// For deletion: the person's actions stay counted, as their signups do,
    /// but nothing they wrote stays. Part of <see cref="VolunteerDataRepository.DeleteAsync"/>'s transaction.
    /// </summary>
    internal static Task ForgetNotesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid volunteerId, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "UPDATE outreach_actions SET note = NULL WHERE volunteer_id = @volunteerId",
            new { volunteerId }, tx, cancellationToken: ct));

    private sealed record Row(
        Guid Id, Guid? VolunteerId, string? VolunteerName, string OfficialKey, string OfficialName,
        int Channel, int? Outcome, string? Note, DateTimeOffset OccurredAt)
    {
        public OutreachAction ToAction() => new(
            Id, VolunteerId, VolunteerName, OfficialKey, OfficialName,
            (OutreachChannel)Channel, (OutreachOutcome?)Outcome, Note, OccurredAt);
    }
}
