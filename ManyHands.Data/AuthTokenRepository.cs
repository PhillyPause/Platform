using System.Security.Cryptography;
using System.Text;
using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// Passwordless sign-in links. Only the SHA-256 hash of a token is stored, so
/// a read of the table cannot be replayed into a sign-in.
/// </summary>
public sealed class AuthTokenRepository(NpgsqlDataSource db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private sealed record RedeemedRow(Guid VolunteerId, string DisplayName);

    /// <summary>Issues a token and returns the raw value — it exists nowhere else.</summary>
    public async Task<string> IssueAsync(Guid volunteerId, CancellationToken ct = default)
    {
        var raw = Tokens.Generate();

        await using var conn = await db.OpenConnectionAsync(ct);

        // One live link at a time: asking for a new one invalidates the old, so
        // a forwarded or leaked older email stops working.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_tokens SET consumed_at = now()
            WHERE volunteer_id = @volunteerId AND consumed_at IS NULL AND expires_at > now()
            """,
            new { volunteerId }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO auth_tokens (volunteer_id, token_hash, expires_at)
            VALUES (@volunteerId, @hash, now() + @lifetime)
            """,
            new { volunteerId, hash = Tokens.Hash(raw), lifetime = Lifetime },
            cancellationToken: ct));

        return raw;
    }

    /// <summary>
    /// Redeems a token, once. The UPDATE ... RETURNING is the atomic claim: two
    /// simultaneous clicks cannot both succeed. A deleted volunteer's tokens
    /// were destroyed with them, so they cannot sign back in.
    /// </summary>
    public async Task<MagicLinkResult> RedeemAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return new MagicLinkResult.Invalid();

        await using var conn = await db.OpenConnectionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<RedeemedRow>(new CommandDefinition(
            """
            UPDATE auth_tokens t
            SET consumed_at = now()
            FROM volunteers v
            WHERE t.token_hash = @hash
              AND t.consumed_at IS NULL
              AND t.expires_at > now()
              AND v.id = t.volunteer_id
              AND NOT v.is_deleted
            RETURNING t.volunteer_id, v.display_name
            """,
            new { hash = Tokens.Hash(rawToken) }, cancellationToken: ct));

        if (row is null)
            return new MagicLinkResult.Invalid();

        // Clicking a link proves control of the mailbox.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET email_verified_at = COALESCE(email_verified_at, now()) WHERE id = @id",
            new { id = row.VolunteerId }, cancellationToken: ct));

        return new MagicLinkResult.Ok(row.VolunteerId, row.DisplayName);
    }

    /// <summary>Housekeeping: consumed and expired tokens have no further use.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM auth_tokens WHERE expires_at < now() - interval '7 days'",
            cancellationToken: ct));
    }
}

/// <summary>Random, URL-safe, single-purpose secrets, stored only as hashes.</summary>
internal static class Tokens
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}
