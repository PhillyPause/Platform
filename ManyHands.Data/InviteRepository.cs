using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>An emailed invitation. The link itself is never here: only who was asked, and whether they came.</summary>
public sealed record Invite(
    Guid Id,
    string Email,
    string? Note,
    string? InvitedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>
    /// Where this invite has got to, as of <paramref name="now"/>. Expiry is the
    /// one transition that happens with nothing writing a row, so the clock is
    /// a parameter: a property that read it would be untestable at the boundary.
    /// </summary>
    public InviteState StateAt(DateTimeOffset now) =>
        RevokedAt is not null ? InviteState.Revoked
        : AcceptedAt is not null ? InviteState.Accepted
        : ExpiresAt <= now ? InviteState.Expired
        : SentAt is null ? InviteState.Queued
        : InviteState.Sent;
}

/// <summary>What someone sees when they open an invite link.</summary>
public sealed record InviteLanding(Guid InviteId, string Email, string? Note, string? InvitedByName);

/// <summary>What came of an organizer pasting a list of addresses in.</summary>
public sealed record InviteBatchResult(int Invited, int AlreadyMembers, int Rejected);

/// <summary>A freshly minted invite link, and what the email needs.</summary>
public sealed record IssuedInvite(string Token, InviteLanding Invite);

/// <summary>
/// Invitations to join. The raw link is minted by the outbox dispatcher when
/// the email is sent, not when the organizer presses Invite, so it exists in
/// exactly one place — the email — and is stored only as a hash.
/// </summary>
public sealed class InviteRepository(NpgsqlDataSource db)
{
    /// <summary>A standing offer, not a sign-in: days, not minutes.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>How many addresses one paste may hold.</summary>
    public const int MaxPerRequest = 100;

    private sealed record ClaimedInvite(Guid InviteId, string Email);

    /// <summary>
    /// Invites everyone in <paramref name="emails"/> who is not already one of
    /// us. Inviting someone with an open invite replaces it, so there is never
    /// more than one live link per address.
    /// </summary>
    public async Task<InviteBatchResult> CreateAsync(
        IReadOnlyCollection<string> emails, string? note, Guid? invitedBy, CancellationToken ct = default)
    {
        var normalized = emails
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(EmailAddress.LooksValid)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxPerRequest)
            .ToList();

        var rejected = emails.Count(e => !EmailAddress.LooksValid(e.Trim()));
        if (normalized.Count == 0) return new InviteBatchResult(0, 0, rejected);

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Somebody who already joined does not need asking again, and pasting
        // last year's list should not spam them.
        var known = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT lower(email) FROM volunteers WHERE lower(email) = ANY(@normalized) AND NOT is_deleted",
            new { normalized = normalized.ToArray() }, tx, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);

        var toInvite = normalized.Where(e => !known.Contains(e)).ToList();

        foreach (var email in toInvite)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE invites SET revoked_at = now()
                WHERE lower(email) = @email AND accepted_at IS NULL AND revoked_at IS NULL
                """,
                new { email }, tx, cancellationToken: ct));

            var inviteId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO invites (email, note, invited_by, expires_at)
                VALUES (@email, @note, @invitedBy, now() + @lifetime)
                RETURNING id
                """,
                new { email, note, invitedBy, lifetime = Lifetime }, tx, cancellationToken: ct));

            await Outbox.QueueAsync(conn, tx, OutboxKinds.InviteSent, new { inviteId }, ct);
        }

        await tx.CommitAsync(ct);
        return new InviteBatchResult(toInvite.Count, normalized.Count - toInvite.Count, rejected);
    }

    public async Task<IReadOnlyList<Invite>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Invite>(new CommandDefinition(
            """
            SELECT i.id, i.email, i.note, v.display_name AS invited_by_name,
                   i.created_at, i.expires_at, i.sent_at, i.accepted_at, i.revoked_at
            FROM invites i
            LEFT JOIN volunteers v ON v.id = i.invited_by
            ORDER BY i.created_at DESC
            LIMIT 500
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Kills the link. The row stays, so "we asked and they never came" is still answerable.</summary>
    public async Task<bool> RevokeAsync(Guid inviteId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE invites SET revoked_at = now() WHERE id = @inviteId AND accepted_at IS NULL AND revoked_at IS NULL",
            new { inviteId }, tx, cancellationToken: ct));

        // A hash nobody can use is a hash not worth keeping.
        if (affected > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM invite_tokens WHERE invite_id = @inviteId",
                new { inviteId }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    /// <summary>
    /// Sends it again and pushes the expiry out. The dispatcher mints a new
    /// link, which kills the one in the earlier email — the rule sign-in
    /// links follow too.
    /// </summary>
    public async Task<bool> ResendAsync(Guid inviteId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invites SET expires_at = now() + @lifetime, sent_at = NULL
            WHERE id = @inviteId AND accepted_at IS NULL AND revoked_at IS NULL
            """,
            new { inviteId, lifetime = Lifetime }, tx, cancellationToken: ct));

        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await Outbox.QueueAsync(conn, tx, OutboxKinds.InviteSent, new { inviteId }, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Mints the link for an invite waiting to go out, and marks it sent. Null
    /// if it was revoked, accepted or expired in the meantime: the outbox is at
    /// least once, so this runs again on redelivery and must find nothing to do.
    /// </summary>
    public async Task<IssuedInvite?> IssueLinkAsync(Guid inviteId, CancellationToken ct = default)
    {
        var raw = Tokens.Generate();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var invite = await conn.QuerySingleOrDefaultAsync<InviteLanding>(new CommandDefinition(
            """
            SELECT i.id AS invite_id, i.email, i.note, v.display_name AS invited_by_name
            FROM invites i
            LEFT JOIN volunteers v ON v.id = i.invited_by
            WHERE i.id = @inviteId AND i.accepted_at IS NULL AND i.revoked_at IS NULL AND i.expires_at > now()
            """,
            new { inviteId }, tx, cancellationToken: ct));

        if (invite is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invite_tokens (invite_id, token_hash) VALUES (@inviteId, @hash)
            ON CONFLICT (invite_id) DO UPDATE SET token_hash = EXCLUDED.token_hash
            """,
            new { inviteId, hash = Tokens.Hash(raw) }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE invites SET sent_at = now() WHERE id = @inviteId",
            new { inviteId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new IssuedInvite(raw, invite);
    }

    /// <summary>What the join page shows somebody arriving from an invite email.</summary>
    public async Task<InviteLanding?> ResolveAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<InviteLanding>(new CommandDefinition(
            """
            SELECT i.id AS invite_id, i.email, i.note, v.display_name AS invited_by_name
            FROM invite_tokens t
            JOIN invites i ON i.id = t.invite_id
            LEFT JOIN volunteers v ON v.id = i.invited_by
            WHERE t.token_hash = @hash AND i.accepted_at IS NULL AND i.revoked_at IS NULL AND i.expires_at > now()
            """,
            new { hash = Tokens.Hash(rawToken) }, cancellationToken: ct));
    }

    /// <summary>
    /// Accepts an invite: the person joins, under the address the invite went
    /// to, with what they said they like doing. The claim is an UPDATE ...
    /// RETURNING, so two simultaneous clicks cannot both count.
    /// </summary>
    public async Task<JoinResult.Ok?> AcceptAsync(
        string rawToken, string displayName, IReadOnlyCollection<short> interestIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var claimed = await conn.QuerySingleOrDefaultAsync<ClaimedInvite>(new CommandDefinition(
            """
            UPDATE invites i SET accepted_at = now()
            FROM invite_tokens t
            WHERE t.invite_id = i.id AND t.token_hash = @hash
              AND i.accepted_at IS NULL AND i.revoked_at IS NULL AND i.expires_at > now()
            RETURNING i.id AS invite_id, i.email
            """,
            new { hash = Tokens.Hash(rawToken) }, tx, cancellationToken: ct));

        if (claimed is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? claimed.Email.Split('@')[0] : displayName.Trim();
        var joined = await VolunteerRepository.JoinCoreAsync(conn, tx, name, claimed.Email, interestIds, ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE invites SET accepted_by = @volunteerId WHERE id = @inviteId",
            new { volunteerId = joined.VolunteerId, inviteId = claimed.InviteId }, tx, cancellationToken: ct));

        // The link has done its job; keeping the hash only widens the window in
        // which a leaked email is worth something.
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM invite_tokens WHERE invite_id = @inviteId",
            new { inviteId = claimed.InviteId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return joined;
    }
}
