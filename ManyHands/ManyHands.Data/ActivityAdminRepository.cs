using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>An activity as an organizer sees it, published or not.</summary>
public sealed record ActivityAdminRow(
    Guid Id,
    string Title,
    ActivityKind Kind,
    string? Location,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? Capacity,
    bool IsPublished,
    int SignupCount,
    int WaitlistCount,
    int CheckedInCount);

/// <summary>Everything the editor needs to put an activity back in front of its organizer.</summary>
public sealed record ActivityDraft(
    Guid Id,
    string Title,
    string? Description,
    ActivityKind Kind,
    string? Location,
    string? MeetingUrl,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? Capacity,
    bool IsPublished,
    short[] TypeIds);

/// <summary>What an organizer fills in. Times are instants; the page converts from Philadelphia's clock.</summary>
public sealed record ActivityFields(
    string Title,
    string? Description,
    ActivityKind Kind,
    string? Location,
    string? MeetingUrl,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? Capacity,
    IReadOnlyCollection<short> TypeIds);

public sealed record RosterEntry(
    Guid SignupId,
    Guid VolunteerId,
    string DisplayName,
    string? Email,
    SignupStatus Status,
    SignupSource Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CheckedInAt);

/// <summary>
/// The organizer's writes and reads. Separate from ActivityRepository so the
/// public read path stays obviously read-only.
/// </summary>
public sealed class ActivityAdminRepository(NpgsqlDataSource db)
{
    public async Task<Guid> CreateAsync(ActivityFields fields, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Always a draft: publishing is its own deliberate step, because it is
        // what tells volunteers.
        var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO activities
              (title, description, kind, location, meeting_url, starts_at, ends_at, capacity, is_published)
            VALUES (@Title, @Description, @kind, @Location, @MeetingUrl, @StartsAt, @EndsAt, @Capacity, false)
            RETURNING id
            """,
            Parameters(fields), tx, cancellationToken: ct));

        await ReplaceTagsAsync(conn, tx, id, fields.TypeIds, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(Guid id, ActivityFields fields, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var parameters = Parameters(fields);
        parameters.Add("id", id);

        var updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE activities
            SET title = @Title, description = @Description, kind = @kind,
                location = @Location, meeting_url = @MeetingUrl,
                starts_at = @StartsAt, ends_at = @EndsAt, capacity = @Capacity
            WHERE id = @id
            """,
            parameters, tx, cancellationToken: ct));

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await ReplaceTagsAsync(conn, tx, id, fields.TypeIds, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Publishing is the moment an activity becomes real to volunteers, so it
    /// queues the announcement. The guard makes it idempotent: publishing
    /// something already published announces nothing. Unpublishing is silent.
    /// </summary>
    public async Task<bool> SetPublishedAsync(Guid id, bool published, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var changed = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE activities SET is_published = @published WHERE id = @id AND is_published <> @published",
            new { id, published }, tx, cancellationToken: ct));

        if (changed == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (published)
            await Outbox.QueueAsync(conn, tx, OutboxKinds.ActivityPublished, new { activityId = id }, ct);

        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Everything, newest first: drafts, published, and what has finished.</summary>
    public async Task<IReadOnlyList<ActivityAdminRow>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ActivityAdminRow>(new CommandDefinition(
            """
            SELECT a.id, a.title, a.kind, a.location, a.starts_at, a.ends_at, a.capacity, a.is_published,
                   (SELECT count(*)::int FROM signups g WHERE g.activity_id = a.id AND g.status = 0) AS signup_count,
                   (SELECT count(*)::int FROM signups g WHERE g.activity_id = a.id AND g.status = 1) AS waitlist_count,
                   (SELECT count(*)::int FROM signups g WHERE g.activity_id = a.id AND g.checked_in_at IS NOT NULL) AS checked_in_count
            FROM activities a
            ORDER BY a.starts_at DESC NULLS FIRST, a.created_at DESC
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ActivityDraft?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ActivityDraft>(new CommandDefinition(
            """
            SELECT a.id, a.title, a.description, a.kind, a.location, a.meeting_url,
                   a.starts_at, a.ends_at, a.capacity, a.is_published,
                   COALESCE((SELECT array_agg(t.activity_type_id ORDER BY t.activity_type_id)
                               FROM activity_tags t WHERE t.activity_id = a.id), '{}'::smallint[]) AS type_ids
            FROM activities a
            WHERE a.id = @id
            """,
            new { id }, cancellationToken: ct));
    }

    /// <summary>Who is coming: the roster, and the list at the door.</summary>
    public async Task<IReadOnlyList<RosterEntry>> GetRosterAsync(Guid activityId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RosterEntry>(new CommandDefinition(
            """
            SELECT g.id AS signup_id, v.id AS volunteer_id, v.display_name, v.email,
                   g.status, g.source, g.created_at, g.checked_in_at
            FROM signups g
            JOIN volunteers v ON v.id = g.volunteer_id
            WHERE g.activity_id = @activityId AND g.status <> 2
            ORDER BY g.status, lower(v.display_name)
            """,
            new { activityId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> SetCheckedInAsync(Guid signupId, bool checkedIn, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE signups
            SET checked_in_at = CASE WHEN @checkedIn THEN COALESCE(checked_in_at, now()) ELSE NULL END
            WHERE id = @signupId AND status <> 2
            """,
            new { signupId, checkedIn }, cancellationToken: ct));
        return rows > 0;
    }

    /// <summary>
    /// Somebody turned up without signing up, and the organizer wants them
    /// counted. They are in, checked in, and over capacity if need be: they
    /// are standing there. No email is fine; with one, they join like anyone
    /// else, and are not sent a confirmation for something they are already at.
    /// </summary>
    public async Task<bool> AddWalkInAsync(
        Guid activityId, string displayName, string? email, CancellationToken ct = default)
    {
        var name = displayName.Trim();
        var address = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (name.Length == 0 || (address is not null && !EmailAddress.LooksValid(address))) return false;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var volunteerId = address is null
            ? await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO volunteers (display_name) VALUES (@name) RETURNING id",
                new { name }, tx, cancellationToken: ct))
            : await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO volunteers (display_name, email) VALUES (@name, @address)
                ON CONFLICT (lower(email)) WHERE email IS NOT NULL
                DO UPDATE SET display_name = COALESCE(NULLIF(volunteers.display_name, ''), EXCLUDED.display_name)
                RETURNING id
                """,
                new { name, address }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO signups (activity_id, volunteer_id, status, source, checked_in_at)
            VALUES (@activityId, @volunteerId, 0, @source, now())
            ON CONFLICT (activity_id, volunteer_id)
            DO UPDATE SET status = 0, checked_in_at = COALESCE(signups.checked_in_at, now())
            """,
            new { activityId, volunteerId, source = (int)SignupSource.Organizer }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return true;
    }

    // Explicit, so the tag list never reaches Dapper as a parameter it might
    // try to expand.
    private static DynamicParameters Parameters(ActivityFields fields)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Title", fields.Title);
        parameters.Add("Description", fields.Description);
        parameters.Add("kind", (int)fields.Kind);
        parameters.Add("Location", fields.Location);
        parameters.Add("MeetingUrl", fields.MeetingUrl);
        // Npgsql writes timestamptz only from UTC, and Dapper's own mapping of
        // DateTimeOffset bypasses the handler that would convert it.
        parameters.Add("StartsAt", fields.StartsAt?.ToUniversalTime());
        parameters.Add("EndsAt", fields.EndsAt?.ToUniversalTime());
        parameters.Add("Capacity", fields.Capacity);
        return parameters;
    }

    private static async Task ReplaceTagsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid activityId,
        IReadOnlyCollection<short> typeIds, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM activity_tags WHERE activity_id = @activityId AND activity_type_id <> ALL(@ids::smallint[])",
            new { activityId, ids = typeIds.ToArray() }, tx, cancellationToken: ct));

        if (typeIds.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO activity_tags (activity_id, activity_type_id)
            SELECT @activityId, t.id FROM activity_types t
            WHERE t.id = ANY(@ids::smallint[]) AND t.is_active
            ON CONFLICT DO NOTHING
            """,
            new { activityId, ids = typeIds.ToArray() }, tx, cancellationToken: ct));
    }
}
