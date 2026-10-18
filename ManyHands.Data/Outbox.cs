using System.Text.Json;
using Dapper;
using Npgsql;

namespace ManyHands.Data;

/// <summary>Writes an outbox row inside the caller's transaction.</summary>
internal static class Outbox
{
    public static Task QueueAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string kind, object payload, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO outbox (kind, payload) VALUES (@kind, @payload::jsonb)",
            new { kind, payload = JsonSerializer.Serialize(payload) }, tx, cancellationToken: ct));
}
