using Dapper;
using Npgsql;

namespace ManyHands.Data;

public sealed record OutboxMessage(long Id, string Kind, string Payload, int Attempts);

/// <summary>
/// The transactional outbox. Claiming is a single atomic UPDATE rather than
/// SELECT ... FOR UPDATE held across the handler: handlers make network calls,
/// and a row lock must not be held for the length of one. A crash after
/// sending but before the row is marked redelivers it, so handlers survive
/// running twice.
/// </summary>
public sealed class OutboxRepository(NpgsqlDataSource db)
{
    public const int MaxAttempts = 8;

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize = 20, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutboxMessage>(new CommandDefinition(
            """
            WITH claimed AS (
              SELECT id FROM outbox
              WHERE processed_at IS NULL AND next_attempt_at <= now()
              ORDER BY id
              LIMIT @batchSize
              FOR UPDATE SKIP LOCKED
            )
            UPDATE outbox o
            SET attempts = o.attempts + 1,
                -- Reserve the row for a while, so a crashed worker's messages
                -- return to the queue instead of being lost or hot-looped.
                next_attempt_at = now() + interval '2 minutes'
            FROM claimed
            WHERE o.id = claimed.id
            RETURNING o.id, o.kind, o.payload::text AS payload, o.attempts
            """,
            new { batchSize }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task MarkProcessedAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox SET processed_at = now(), last_error = NULL WHERE id = @id",
            new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// Exponential backoff, capped. After <see cref="MaxAttempts"/> the row is
    /// parked with a far-future retry rather than deleted: a poisoned message
    /// stays visible instead of vanishing.
    /// </summary>
    public async Task MarkFailedAsync(long id, int attempts, string error, CancellationToken ct = default)
    {
        var delay = attempts >= MaxAttempts
            ? TimeSpan.FromDays(3650)
            : TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, attempts) * 10));

        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE outbox
            SET last_error = @error, next_attempt_at = now() + @delay
            WHERE id = @id
            """,
            new { id, error = error.Length <= 2000 ? error : error[..2000], delay },
            cancellationToken: ct));
    }

    /// <summary>Queues a message outside any other write.</summary>
    public async Task QueueAsync(string kind, object payload, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await Outbox.QueueAsync(conn, tx: null, kind, payload, ct);
    }
}
