using System.Text.Json;
using Dapper;
using Npgsql;

namespace ManyHands.Data;

/// <summary>Writes an outbox row inside the caller's transaction.</summary>
internal static class Outbox
{
    /// <summary>
    /// Queues a message. With a <paramref name="delay"/> the worker leaves the
    /// row alone until then, which is how a later message is booked by the
    /// write that causes it.
    /// </summary>
    public static Task QueueAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string kind, object payload, CancellationToken ct,
        TimeSpan? delay = null) =>
        conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox (kind, payload, next_attempt_at)
            VALUES (@kind, @payload::jsonb, now() + @delay)
            """,
            new { kind, payload = JsonSerializer.Serialize(payload), delay = delay ?? TimeSpan.Zero },
            tx, cancellationToken: ct));
}
