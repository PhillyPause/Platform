using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data.Discord;

/// <summary>One role change the sweep should make.</summary>
public sealed record DiscordRoleAction(Guid VolunteerId, ulong DiscordUserId, ulong RoleId, bool IsGrant);

/// <summary>
/// What the bot needs and nothing else does: matching a Discord account to a
/// volunteer, remembering which activities have been announced, and which
/// roles the bot handed out.
/// </summary>
public sealed class DiscordQueries(NpgsqlDataSource db)
{
    /// <summary>The <c>external_publications.target</c> Discord announcements are recorded under.</summary>
    private const string Target = "discord";

    public async Task<Volunteer?> GetVolunteerByDiscordIdAsync(ulong discordUserId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Volunteer>(new CommandDefinition(
            $"SELECT {VolunteerRepository.Columns} FROM volunteers WHERE discord_user_id = @discordUserId AND NOT is_deleted",
            new { discordUserId }, cancellationToken: ct));
    }

    /// <summary>
    /// Links a Discord account to a volunteer. An account belongs to one person,
    /// so linking it here unlinks it from anyone else: both sides have just
    /// proved they are the same person.
    /// </summary>
    public async Task LinkAsync(Guid volunteerId, ulong discordUserId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET discord_user_id = NULL WHERE discord_user_id = @discordUserId AND id <> @volunteerId",
            new { discordUserId, volunteerId }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET discord_user_id = @discordUserId WHERE id = @volunteerId AND NOT is_deleted",
            new { discordUserId, volunteerId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task UnlinkAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE volunteers SET discord_user_id = NULL WHERE id = @volunteerId",
            new { volunteerId }, cancellationToken: ct));
    }

    /// <summary>
    /// Takes the right to announce an activity, once. Discord has no
    /// idempotency key, so the guard lives here. A claim never completed
    /// (synced_at null) belongs to an attempt that died, and is handed out
    /// again — leaving one narrow window that can double-post: a crash after
    /// Discord took the message but before we recorded it. Losing the
    /// announcement outright would be worse.
    /// </summary>
    public async Task<bool> TryClaimAnnouncementAsync(Guid activityId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            INSERT INTO external_publications (activity_id, target) VALUES (@activityId, @target)
            ON CONFLICT (activity_id, target) DO UPDATE SET last_error = NULL
              WHERE external_publications.synced_at IS NULL
            RETURNING id
            """,
            new { activityId, target = Target }, cancellationToken: ct)) is not null;
    }

    /// <summary>Closes the claim, recording where the post landed. Must be the last thing that happens.</summary>
    public async Task MarkAnnouncedAsync(
        Guid activityId, ulong? messageId, ulong? threadId, string? url, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE external_publications
            SET synced_at    = now(),
                last_error   = NULL,
                external_id  = COALESCE(@messageId::bigint::text, external_id),
                external_url = COALESCE(@url, external_url),
                thread_id    = COALESCE(@threadId::bigint, thread_id)
            WHERE activity_id = @activityId AND target = @target
            """,
            new { activityId, target = Target, messageId = Snowflake(messageId), threadId = Snowflake(threadId), url },
            cancellationToken: ct));
    }

    /// <summary>Leaves the claim open for the outbox retry, and says why.</summary>
    public async Task MarkAnnouncementFailedAsync(Guid activityId, string error, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE external_publications SET last_error = @error WHERE activity_id = @activityId AND target = @target",
            new { activityId, target = Target, error = error.Length <= 2000 ? error : error[..2000] }, cancellationToken: ct));
    }

    public async Task<ulong?> GetAnnouncementThreadAsync(Guid activityId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var threadId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT thread_id FROM external_publications WHERE activity_id = @activityId AND target = @target",
            new { activityId, target = Target }, cancellationToken: ct));
        return threadId is long id ? unchecked((ulong)id) : null;
    }

    /// <summary>
    /// Every role that disagrees with ManyHands. Grants: linked volunteers with
    /// no grant on record for the current role. Revokes come only from grants
    /// the bot made, because it cannot ask Discord who holds a role — listing
    /// members needs a privileged intent the bot does not request. A role a
    /// person handed out by hand is therefore never taken away, which is the
    /// right side to err on. Revokes come first, so a changed role id loses the
    /// old role in the same sweep that grants the new.
    /// </summary>
    public async Task<IReadOnlyList<DiscordRoleAction>> GetRoleSyncActionsAsync(
        ulong? roleId, int limit = 500, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DiscordRoleAction>(new CommandDefinition(
            """
            SELECT v.id AS volunteer_id, v.discord_user_id, @roleId::bigint AS role_id, true AS is_grant
            FROM volunteers v
            WHERE @roleId::bigint IS NOT NULL
              AND NOT v.is_deleted
              AND v.discord_user_id IS NOT NULL
              AND NOT EXISTS (
                    SELECT 1 FROM discord_role_grants g
                     WHERE g.volunteer_id = v.id
                       AND g.role_id = @roleId::bigint
                       AND g.discord_user_id = v.discord_user_id)

            UNION ALL

            SELECT g.volunteer_id, g.discord_user_id, g.role_id, false AS is_grant
            FROM discord_role_grants g
            WHERE g.role_id IS DISTINCT FROM @roleId::bigint
               OR NOT EXISTS (SELECT 1 FROM volunteers v
                               WHERE v.id = g.volunteer_id
                                 AND NOT v.is_deleted
                                 AND v.discord_user_id = g.discord_user_id)

            ORDER BY is_grant
            LIMIT @limit
            """,
            new { roleId = Snowflake(roleId), limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Remembers that this person holds the role because the bot said so.</summary>
    public async Task RecordRoleGrantAsync(Guid volunteerId, ulong discordUserId, ulong roleId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO discord_role_grants (volunteer_id, discord_user_id, role_id) VALUES (@volunteerId, @discordUserId, @roleId)
            ON CONFLICT (volunteer_id) DO UPDATE
              SET discord_user_id = EXCLUDED.discord_user_id, role_id = EXCLUDED.role_id, granted_at = now()
            """,
            new { volunteerId, discordUserId, roleId }, cancellationToken: ct));
    }

    public async Task ClearRoleGrantAsync(Guid volunteerId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM discord_role_grants WHERE volunteer_id = @volunteerId",
            new { volunteerId }, cancellationToken: ct));
    }

    /// <summary>
    /// Snowflakes bind as <c>long?</c>: the ulong handler converts a value, but
    /// a null carries none for it to convert. Every real snowflake fits in a
    /// positive long, so the cast is lossless.
    /// </summary>
    private static long? Snowflake(ulong? id) => id is ulong value ? unchecked((long)value) : null;
}
