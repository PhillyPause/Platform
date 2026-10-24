using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// What the members page does to a person: change their role, delete them, or
/// send the joining email again. Each action reads the role of whoever asked
/// fresh from the database, never from the page that offered the button: the
/// page is a long-lived circuit, and a removed organizer must stop being one
/// at their next click.
/// </summary>
public sealed class MemberAdminRepository(NpgsqlDataSource db, VolunteerRepository volunteers, VolunteerDataRepository data)
{
    /// <summary>A second press within this long sends nothing: a double click is not a second request.</summary>
    public static readonly TimeSpan ResendWindow = TimeSpan.FromMinutes(15);

    /// <summary>Admins alone, and someone has to be left holding the keys.</summary>
    public async Task<MemberActionResult> SetRoleAsync(
        Guid actorId, Guid volunteerId, VolunteerRole role, CancellationToken ct = default)
    {
        if (!await IsAtLeastAsync(actorId, VolunteerRole.Admin, ct)) return MemberActionResult.NotAllowed;

        // Someone who came in through Discord alone has no address to sign in with.
        if (await volunteers.GetByIdAsync(volunteerId, ct) is not { IsDeleted: false, Email: not null } target)
            return MemberActionResult.NotFound;

        if (target.Role == role) return MemberActionResult.Done;

        if (target.Role == VolunteerRole.Admin && await volunteers.CountAdminsAsync(ct) <= 1)
            return MemberActionResult.LastAdmin;

        return await volunteers.SetRoleAsync(volunteerId, role, ct) ? MemberActionResult.Done : MemberActionResult.NotFound;
    }

    /// <summary>
    /// The same deletion as the person's own button: it anonymises the row and
    /// cannot be undone. Admins alone, never themselves, and never another
    /// admin: they are made an organizer first, which the last-admin rule guards.
    /// </summary>
    public async Task<MemberActionResult> DeleteAsync(Guid actorId, Guid volunteerId, CancellationToken ct = default)
    {
        if (!await IsAtLeastAsync(actorId, VolunteerRole.Admin, ct)) return MemberActionResult.NotAllowed;
        if (actorId == volunteerId) return MemberActionResult.Self;

        if (await volunteers.GetByIdAsync(volunteerId, ct) is not { IsDeleted: false } target)
            return MemberActionResult.NotFound;
        if (target.Role == VolunteerRole.Admin) return MemberActionResult.IsAdmin;

        return await data.DeleteAsync(volunteerId, ct) ? MemberActionResult.Done : MemberActionResult.NotFound;
    }

    /// <summary>
    /// Queues the joining email again, as joining again would: a fresh confirm
    /// link beside the removal link. Organizers may, since it changes nothing
    /// but the person's inbox. Nobody confirmed, unsubscribed or without an
    /// address, and not twice inside <see cref="ResendWindow"/>.
    /// </summary>
    public async Task<MemberActionResult> ResendConfirmationAsync(
        Guid actorId, Guid volunteerId, CancellationToken ct = default)
    {
        if (!await IsAtLeastAsync(actorId, VolunteerRole.Organizer, ct)) return MemberActionResult.NotAllowed;

        if (await volunteers.GetByIdAsync(volunteerId, ct) is not { IsDeleted: false } target)
            return MemberActionResult.NotFound;
        if (target.EmailVerifiedAt is not null) return MemberActionResult.AlreadyConfirmed;
        if (!target.CanReceiveEmail) return MemberActionResult.CannotEmail;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Two presses at once would both see nothing recent; the lock makes
        // the second wait and then see the first.
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext('member.resend:' || @id))",
            new { id = volunteerId.ToString() }, tx, cancellationToken: ct));

        var recent = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM outbox
                WHERE kind = @kind AND payload->>'volunteerId' = @id AND created_at > now() - @window)
            """,
            new { kind = OutboxKinds.MemberConfirm, id = volunteerId.ToString(), window = ResendWindow },
            tx, cancellationToken: ct));
        if (recent)
        {
            await tx.RollbackAsync(ct);
            return MemberActionResult.RecentlySent;
        }

        await Outbox.QueueAsync(conn, tx, OutboxKinds.MemberConfirm, new { volunteerId }, ct);
        await tx.CommitAsync(ct);
        return MemberActionResult.Done;
    }

    private async Task<bool> IsAtLeastAsync(Guid actorId, VolunteerRole role, CancellationToken ct) =>
        await volunteers.GetByIdAsync(actorId, ct) is { } actor && actor.IsAtLeast(role);
}

public enum MemberActionResult
{
    Done,
    /// <summary>Whoever asked does not hold the role the action needs.</summary>
    NotAllowed,
    /// <summary>No such person, deleted already, or no address to act on.</summary>
    NotFound,
    LastAdmin,
    /// <summary>An admin is made an organizer before they can be deleted.</summary>
    IsAdmin,
    /// <summary>Nobody deletes themselves from here; their own page has the button.</summary>
    Self,
    AlreadyConfirmed,
    /// <summary>Unsubscribed, or no address.</summary>
    CannotEmail,
    RecentlySent,
}
