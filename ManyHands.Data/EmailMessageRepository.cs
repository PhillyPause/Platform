using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// Every message ManyHands sends a volunteer is recorded here before it
/// leaves, which makes "did they get it?" a query instead of a guess. The
/// bodies are personal correspondence: no page shows them.
/// </summary>
public sealed class EmailMessageRepository(NpgsqlDataSource db)
{
    public async Task<Guid> QueueAsync(
        Guid? volunteerId, string toAddress, string subject, string bodyText, string kind,
        CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO email_messages (volunteer_id, to_address, subject, body_text, kind, status)
            VALUES (@volunteerId, @toAddress, @subject, @bodyText, @kind, 0)
            RETURNING id
            """,
            new { volunteerId, toAddress, subject, bodyText, kind },
            cancellationToken: ct));
    }

    /// <summary>
    /// How many messages this address has been sent or queued recently. The
    /// flood cap reads it; suppressed rows count too, so suppression cannot be
    /// reset by more abuse.
    /// </summary>
    public async Task<int> CountRecentAsync(string toAddress, TimeSpan window, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int FROM email_messages
            WHERE lower(to_address) = lower(@toAddress)
              AND created_at > now() - @window
            """,
            new { toAddress, window }, cancellationToken: ct));
    }

    public async Task MarkSentAsync(
        Guid id, EmailStatus status, string? providerMessageId, string? error,
        CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE email_messages
            SET status = @status,
                provider_message_id = @providerMessageId,
                error = @error,
                sent_at = CASE WHEN @status IN (1, 3) THEN now() ELSE sent_at END
            WHERE id = @id
            """,
            new { id, status = (int)status, providerMessageId, error },
            cancellationToken: ct));
    }
}
