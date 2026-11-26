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

    /// <summary>
    /// A confirmation link waits in an inbox for a volunteer to get round to
    /// it, so it lasts a month rather than twenty minutes.
    /// </summary>
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromDays(30);

    private const string SignInPurpose = "magic-link";
    private const string CodePurpose = "sign-in-code";
    private const string ConfirmPurpose = "confirm";

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
            WHERE volunteer_id = @volunteerId AND purpose = @purpose
              AND consumed_at IS NULL AND expires_at > now()
            """,
            new { volunteerId, purpose = SignInPurpose }, cancellationToken: ct));

        await InsertAsync(conn, volunteerId, raw, SignInPurpose, Lifetime, ct);
        return raw;
    }

    /// <summary>
    /// Issues the six-digit code that travels in the same email as a sign-in
    /// link, for where a link cannot reach: a site installed to a phone's home
    /// screen keeps its own cookies, so a link opened from mail signs in the
    /// browser rather than the app. Stored as a hash of the volunteer's id and
    /// the digits, so the same six digits for two people are two rows and a
    /// read of the table gives nothing away. Asking again replaces the last
    /// code, which is deleted rather than spent: only one ever exists per
    /// person, so two that happen to share digits cannot meet on the hash's
    /// unique key.
    /// </summary>
    public async Task<string> IssueCodeAsync(Guid volunteerId, CancellationToken ct = default)
    {
        var code = SignInCode.Generate();

        await using var conn = await db.OpenConnectionAsync(ct);
        await ForgetCodeAsync(conn, volunteerId, ct);
        await InsertAsync(conn, volunteerId, SignInCode.Key(volunteerId, code), CodePurpose, Lifetime, ct);
        return code;
    }

    /// <summary>
    /// Signs in with a code, once, and spends the link that came in the same
    /// email: one email, one sign-in. A code is only ever tried against the
    /// person whose address was typed, so a guess cannot land on someone else.
    /// </summary>
    public async Task<MagicLinkResult> RedeemCodeAsync(Guid volunteerId, string typed, CancellationToken ct = default)
    {
        if (SignInCode.Normalize(typed) is not { } code)
            return new MagicLinkResult.Invalid();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<RedeemedRow>(new CommandDefinition(
            """
            UPDATE auth_tokens t
            SET consumed_at = now()
            FROM volunteers v
            WHERE t.token_hash = @hash
              AND t.purpose = @purpose
              AND t.volunteer_id = @volunteerId
              AND t.consumed_at IS NULL
              AND t.expires_at > now()
              AND v.id = t.volunteer_id
              AND NOT v.is_deleted
            RETURNING t.volunteer_id, v.display_name
            """,
            new { hash = Tokens.Hash(SignInCode.Key(volunteerId, code)), purpose = CodePurpose, volunteerId },
            tx, cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return new MagicLinkResult.Invalid();
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_tokens SET consumed_at = now()
            WHERE volunteer_id = @volunteerId AND purpose = @purpose AND consumed_at IS NULL;
            UPDATE volunteers SET email_verified_at = COALESCE(email_verified_at, now()) WHERE id = @volunteerId;
            """,
            new { volunteerId, purpose = SignInPurpose }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new MagicLinkResult.Ok(row.VolunteerId, row.DisplayName);
    }

    /// <summary>
    /// Throws away a person's code, for when too many wrong ones have been
    /// tried against it: they ask for a fresh email, and the guessing starts
    /// over against a code nobody has seen.
    /// </summary>
    public async Task ForgetCodeAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await ForgetCodeAsync(conn, volunteerId, ct);
    }

    private static Task ForgetCodeAsync(NpgsqlConnection conn, Guid volunteerId, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM auth_tokens WHERE volunteer_id = @volunteerId AND purpose = @purpose",
            new { volunteerId, purpose = CodePurpose }, cancellationToken: ct));

    /// <summary>
    /// Issues the link in a joining email that says "this address is mine".
    /// Unlike a sign-in link it leaves earlier ones working: whichever of
    /// their emails a volunteer finds first should do.
    /// </summary>
    public async Task<string> IssueConfirmationAsync(Guid volunteerId, CancellationToken ct = default)
    {
        var raw = Tokens.Generate();

        await using var conn = await db.OpenConnectionAsync(ct);
        await InsertAsync(conn, volunteerId, raw, ConfirmPurpose, ConfirmationLifetime, ct);
        return raw;
    }

    private static Task InsertAsync(
        NpgsqlConnection conn, Guid volunteerId, string raw, string purpose, TimeSpan lifetime, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO auth_tokens (volunteer_id, token_hash, purpose, expires_at)
            VALUES (@volunteerId, @hash, @purpose, now() + @lifetime)
            """,
            new { volunteerId, hash = Tokens.Hash(raw), purpose, lifetime },
            cancellationToken: ct));

    /// <summary>
    /// Who a confirmation link is for, without spending it: the page that
    /// asks "is this you?" greets them by name and offers "that's not me".
    /// Null for a link that would not confirm anyone.
    /// </summary>
    public async Task<PendingConfirmation?> PeekConfirmationAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PendingConfirmation>(new CommandDefinition(
            """
            SELECT v.id AS volunteer_id, v.display_name, v.comms_key
            FROM auth_tokens t
            JOIN volunteers v ON v.id = t.volunteer_id
            WHERE t.token_hash = @hash
              AND t.purpose = @purpose
              AND t.consumed_at IS NULL
              AND t.expires_at > now()
              AND v.email_verified_at IS NULL
              AND NOT v.is_deleted
            """,
            new { hash = Tokens.Hash(rawToken), purpose = ConfirmPurpose }, cancellationToken: ct));
    }

    /// <summary>
    /// Confirms the address a confirmation link went to, once. It works only
    /// while the address is unconfirmed, and spends every confirmation link
    /// the person holds, so a month-old email cannot sign anyone in later.
    /// </summary>
    public async Task<MagicLinkResult> ConfirmAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return new MagicLinkResult.Invalid();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Setting email_verified_at is the claim: two presses cannot both see
        // it empty.
        var row = await conn.QuerySingleOrDefaultAsync<RedeemedRow>(new CommandDefinition(
            """
            UPDATE volunteers v
            SET email_verified_at = now()
            FROM auth_tokens t
            WHERE t.token_hash = @hash
              AND t.purpose = @purpose
              AND t.consumed_at IS NULL
              AND t.expires_at > now()
              AND v.id = t.volunteer_id
              AND v.email_verified_at IS NULL
              AND NOT v.is_deleted
            RETURNING v.id AS volunteer_id, v.display_name
            """,
            new { hash = Tokens.Hash(rawToken), purpose = ConfirmPurpose }, tx, cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return new MagicLinkResult.Invalid();
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_tokens SET consumed_at = now()
            WHERE volunteer_id = @volunteerId AND purpose = @purpose AND consumed_at IS NULL
            """,
            new { volunteerId = row.VolunteerId, purpose = ConfirmPurpose }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new MagicLinkResult.Ok(row.VolunteerId, row.DisplayName);
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
              AND t.purpose = @purpose
              AND t.consumed_at IS NULL
              AND t.expires_at > now()
              AND v.id = t.volunteer_id
              AND NOT v.is_deleted
            RETURNING t.volunteer_id, v.display_name
            """,
            new { hash = Tokens.Hash(rawToken), purpose = SignInPurpose }, cancellationToken: ct));

        if (row is null)
            return new MagicLinkResult.Invalid();

        // Clicking a link proves control of the mailbox, and spends the code
        // that came with it.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE volunteers SET email_verified_at = COALESCE(email_verified_at, now()) WHERE id = @id;
            UPDATE auth_tokens SET consumed_at = now()
            WHERE volunteer_id = @id AND purpose = @codePurpose AND consumed_at IS NULL;
            """,
            new { id = row.VolunteerId, codePurpose = CodePurpose }, cancellationToken: ct));

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

/// <summary>The person an unspent confirmation link is for.</summary>
public sealed record PendingConfirmation(Guid VolunteerId, string DisplayName, Guid CommsKey);

/// <summary>Random, URL-safe, single-purpose secrets, stored only as hashes.</summary>
internal static class Tokens
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}

/// <summary>
/// The six digits in a sign-in email. One in a million to guess, so the
/// guessing is capped where they are typed (SignInCodeAttempts) as well as
/// by the twenty minutes a code lives.
/// </summary>
public static class SignInCode
{
    public const int Digits = 6;

    internal static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"123 456" in an email: two halves are easier to carry to another screen.</summary>
    public static string Show(string code) => $"{code[..3]} {code[3..]}";

    /// <summary>
    /// The digits someone typed, with spaces and dashes taken out, as they
    /// might copy "123 456"; null for anything that is not six digits.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (typed is null) return null;
        var digits = string.Concat(typed.Where(c => c is not (' ' or '-')));
        return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    internal static string Key(Guid volunteerId, string code) => $"{volunteerId:N}:{code}";
}
