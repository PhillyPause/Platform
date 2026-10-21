using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// The federal directory: the members of Congress, refreshed weekly from a
/// public dataset. Reference data, not anybody's record, so no page can
/// change it; organizers browse it and import an office into the CRM.
/// </summary>
public sealed class FederalOfficialsRepository(NpgsqlDataSource db)
{
    private const string Columns =
        """
        bioguide_id, full_name, chamber, state, district, party,
        term_ends_on, website, phone, office
        """;

    public async Task<IReadOnlyList<FederalOfficial>> SearchAsync(
        string? state = null, string? chamber = null, string? search = null,
        CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<FederalOfficial>(new CommandDefinition(
            $"""
             SELECT {Columns}
             FROM federal_officials
             WHERE (@state::text IS NULL OR state = @state)
               AND (@chamber::text IS NULL OR chamber = @chamber)
               AND (@search::text IS NULL OR full_name ILIKE '%' || @search || '%')
             ORDER BY state, chamber DESC, full_name
             LIMIT 200
             """,
            new { state, chamber, search }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<FederalOfficial?> GetAsync(string bioguideId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<FederalOfficial>(new CommandDefinition(
            $"SELECT {Columns} FROM federal_officials WHERE bioguide_id = @bioguideId",
            new { bioguideId }, cancellationToken: ct));
    }

    /// <summary>
    /// Upsert one official. The sync replays the whole dataset, so this must be
    /// idempotent — a member who has not changed simply gets a new updated_at.
    /// </summary>
    public async Task UpsertAsync(FederalOfficial official, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO federal_officials
              (bioguide_id, full_name, chamber, state, district, party,
               term_ends_on, website, phone, office, updated_at)
            VALUES (@BioguideId, @FullName, @Chamber, @State, @District, @Party,
                    @TermEndsOn, @Website, @Phone, @Office, now())
            ON CONFLICT (bioguide_id) DO UPDATE SET
              full_name    = EXCLUDED.full_name,
              chamber      = EXCLUDED.chamber,
              state        = EXCLUDED.state,
              district     = EXCLUDED.district,
              party        = EXCLUDED.party,
              term_ends_on = EXCLUDED.term_ends_on,
              website      = EXCLUDED.website,
              phone        = EXCLUDED.phone,
              office       = EXCLUDED.office,
              updated_at   = now()
            """,
            official, cancellationToken: ct));
    }

    public async Task<long> StartSyncAsync(string source, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO sync_runs (source) VALUES (@source) RETURNING id",
            new { source }, cancellationToken: ct));
    }

    public async Task FinishSyncAsync(
        long id, int records, string? error, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sync_runs
            SET finished_at = now(), records = @records, error = @error
            WHERE id = @id
            """,
            new { id, records, error }, cancellationToken: ct));
    }

    /// <summary>How stale the directory is — the sync worker's skip check.</summary>
    public async Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var value = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            """
            SELECT max(finished_at) FROM sync_runs
            WHERE error IS NULL AND finished_at IS NOT NULL
            """,
            cancellationToken: ct));

        return value is DateTime dt
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : null;
    }
}
