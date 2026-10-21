using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>What an organizer fills in for an organization.</summary>
public sealed record OrganizationFields(
    OrganizationKind Kind, string Name, string? Description, string? Website,
    string? Address, string? State, string? District);

/// <summary>What an organizer fills in for an engagement.</summary>
public sealed record EngagementFields(
    Guid? OrganizationId, string Title, EngagementStatus Status, ActivityCategory? ActivityCategory,
    Guid? OwnerId, DateTimeOffset? ScheduledAt, string? Location, string? MeetingUrl, string? Summary);

/// <summary>
/// The CRM. Organizations, contacts, engagements and tasks are soft-deleted:
/// a staffer who left still explains a conversation from last March, so rows
/// are hidden, not destroyed. Interactions are never deleted: the timeline is
/// the point.
/// </summary>
public sealed class CrmRepository(NpgsqlDataSource db)
{
    // --- organizations -----------------------------------------------------

    public async Task<IReadOnlyList<OrganizationSummary>> GetOrganizationsAsync(
        OrganizationKind? kind = null, string? search = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OrganizationSummary>(new CommandDefinition(
            """
            SELECT o.id, o.kind, o.name, o.state, o.district,
                   (SELECT count(*)::int FROM crm_contacts c
                     WHERE c.organization_id = o.id AND NOT c.is_deleted)           AS contact_count,
                   (SELECT count(*)::int FROM crm_engagements e
                     WHERE e.organization_id = o.id AND NOT e.is_deleted
                       AND e.status NOT IN (4, 6))                                  AS open_engagement_count,
                   (SELECT max(i.occurred_at) FROM crm_interactions i
                     WHERE i.organization_id = o.id)                                AS last_interaction_at
            FROM crm_organizations o
            WHERE NOT o.is_deleted
              AND (@kind::int IS NULL OR o.kind = @kind)
              AND (@search::text IS NULL OR o.name ILIKE '%' || @search || '%')
            ORDER BY o.name
            """,
            new { kind = (int?)kind, search = Blank(search) }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CrmOrganization?> GetOrganizationAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CrmOrganization>(new CommandDefinition(
            """
            SELECT id, kind, name, description, website, address, state, district, external_ref, created_at
            FROM crm_organizations WHERE id = @id AND NOT is_deleted
            """,
            new { id }, cancellationToken: ct));
    }

    public async Task<Guid> CreateOrganizationAsync(
        OrganizationFields fields, string? externalRef, Guid? createdBy, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        // external_ref makes importing a federal official idempotent: importing
        // the same office twice returns the row instead of duplicating it.
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_organizations
              (kind, name, description, website, address, state, district, external_ref, created_by)
            VALUES (@kind, @Name, @Description, @Website, @Address, @State, @District, @externalRef, @createdBy)
            ON CONFLICT (external_ref) WHERE external_ref IS NOT NULL
            DO UPDATE SET name = EXCLUDED.name, is_deleted = false, deleted_at = NULL
            RETURNING id
            """,
            new
            {
                kind = (int)fields.Kind, fields.Name, fields.Description, fields.Website, fields.Address,
                fields.State, fields.District, externalRef, createdBy
            },
            cancellationToken: ct));
    }

    public async Task<bool> UpdateOrganizationAsync(Guid id, OrganizationFields fields, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crm_organizations
            SET kind = @kind, name = @Name, description = @Description, website = @Website,
                address = @Address, state = @State, district = @District
            WHERE id = @id AND NOT is_deleted
            """,
            new
            {
                id, kind = (int)fields.Kind, fields.Name, fields.Description, fields.Website,
                fields.Address, fields.State, fields.District
            },
            cancellationToken: ct)) > 0;
    }

    public Task<bool> DeleteOrganizationAsync(Guid id, CancellationToken ct = default) =>
        SoftDeleteAsync("crm_organizations", id, ct);

    // --- contacts ----------------------------------------------------------

    public async Task<IReadOnlyList<CrmContact>> GetContactsAsync(
        Guid? organizationId = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CrmContact>(new CommandDefinition(
            """
            SELECT id, organization_id, full_name, role_title, email, phone, notes, created_at
            FROM crm_contacts
            WHERE NOT is_deleted AND (@organizationId::uuid IS NULL OR organization_id = @organizationId)
            ORDER BY full_name
            """,
            new { organizationId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CrmContact?> GetContactAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CrmContact>(new CommandDefinition(
            """
            SELECT id, organization_id, full_name, role_title, email, phone, notes, created_at
            FROM crm_contacts WHERE id = @id AND NOT is_deleted
            """,
            new { id }, cancellationToken: ct));
    }

    public async Task<Guid> SaveContactAsync(
        Guid? id, Guid? organizationId, string fullName, string? roleTitle, string? email,
        string? phone, string? notes, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        if (id is Guid existing)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE crm_contacts
                SET organization_id = @organizationId, full_name = @fullName, role_title = @roleTitle,
                    email = @email, phone = @phone, notes = @notes
                WHERE id = @existing AND NOT is_deleted
                """,
                new { existing, organizationId, fullName, roleTitle, email, phone, notes }, cancellationToken: ct));
            return existing;
        }

        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_contacts (organization_id, full_name, role_title, email, phone, notes)
            VALUES (@organizationId, @fullName, @roleTitle, @email, @phone, @notes)
            RETURNING id
            """,
            new { organizationId, fullName, roleTitle, email, phone, notes }, cancellationToken: ct));
    }

    public Task<bool> DeleteContactAsync(Guid id, CancellationToken ct = default) =>
        SoftDeleteAsync("crm_contacts", id, ct);

    // --- engagements -------------------------------------------------------

    public async Task<IReadOnlyList<EngagementSummary>> GetEngagementsAsync(
        Guid? organizationId = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<EngagementSummary>(new CommandDefinition(
            """
            SELECT e.id, e.title, e.status, e.activity_category,
                   e.organization_id,
                   o.name         AS organization_name,
                   v.display_name AS owner_name,
                   e.scheduled_at,
                   (SELECT count(*)::int FROM crm_tasks t
                     WHERE t.engagement_id = e.id AND NOT t.is_deleted AND t.status = 0) AS open_task_count
            FROM crm_engagements e
            LEFT JOIN crm_organizations o ON o.id = e.organization_id
            LEFT JOIN volunteers v        ON v.id = e.owner_id
            WHERE NOT e.is_deleted AND (@organizationId::uuid IS NULL OR e.organization_id = @organizationId)
            ORDER BY e.scheduled_at NULLS LAST, e.created_at DESC
            """,
            new { organizationId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CrmEngagement?> GetEngagementAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CrmEngagement>(new CommandDefinition(
            """
            SELECT id, organization_id, title, status, activity_category, owner_id,
                   scheduled_at, location, meeting_url, summary, created_at
            FROM crm_engagements WHERE id = @id AND NOT is_deleted
            """,
            new { id }, cancellationToken: ct));
    }

    public async Task<Guid> SaveEngagementAsync(Guid? id, EngagementFields fields, CancellationToken ct = default)
    {
        var parameters = new
        {
            existing = id,
            fields.OrganizationId,
            fields.Title,
            status = (int)fields.Status,
            activityCategory = (int?)fields.ActivityCategory,
            fields.OwnerId,
            scheduledAt = fields.ScheduledAt?.ToUniversalTime(),
            fields.Location,
            fields.MeetingUrl,
            fields.Summary
        };

        await using var conn = await db.OpenConnectionAsync(ct);

        if (id is Guid existing)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE crm_engagements
                SET organization_id = @OrganizationId, title = @Title, status = @status,
                    activity_category = @activityCategory, owner_id = @OwnerId, scheduled_at = @scheduledAt,
                    location = @Location, meeting_url = @MeetingUrl, summary = @Summary
                WHERE id = @existing AND NOT is_deleted
                """,
                parameters, cancellationToken: ct));
            return existing;
        }

        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_engagements
              (organization_id, title, status, activity_category, owner_id, scheduled_at, location, meeting_url, summary)
            VALUES (@OrganizationId, @Title, @status, @activityCategory, @OwnerId, @scheduledAt, @Location, @MeetingUrl, @Summary)
            RETURNING id
            """,
            parameters, cancellationToken: ct));
    }

    /// <summary>Moving a card on the board.</summary>
    public async Task<bool> SetEngagementStatusAsync(Guid id, EngagementStatus status, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE crm_engagements SET status = @status WHERE id = @id AND NOT is_deleted",
            new { id, status = (int)status }, cancellationToken: ct)) > 0;
    }

    public Task<bool> DeleteEngagementAsync(Guid id, CancellationToken ct = default) =>
        SoftDeleteAsync("crm_engagements", id, ct);

    // --- interactions ------------------------------------------------------

    public async Task<Guid> LogInteractionAsync(
        Guid? organizationId, Guid? contactId, Guid? engagementId, InteractionKind kind,
        DateTimeOffset occurredAt, string body, Guid? loggedBy, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_interactions (organization_id, contact_id, engagement_id, kind, occurred_at, body, logged_by)
            VALUES (@organizationId, @contactId, @engagementId, @kind, @occurredAt, @body, @loggedBy)
            RETURNING id
            """,
            new
            {
                organizationId, contactId, engagementId, kind = (int)kind,
                occurredAt = occurredAt.ToUniversalTime(), body, loggedBy
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// The timeline. An interaction logged against a contact also shows on
    /// their organization, because "what have we said to this office" is the
    /// question people actually ask.
    /// </summary>
    public async Task<IReadOnlyList<TimelineEntry>> GetTimelineAsync(
        Guid? organizationId = null, Guid? contactId = null, Guid? engagementId = null,
        int limit = 100, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TimelineEntry>(new CommandDefinition(
            """
            SELECT i.id, i.kind, i.occurred_at, i.body,
                   v.display_name AS logged_by_name,
                   c.full_name    AS contact_name,
                   e.title        AS engagement_title
            FROM crm_interactions i
            LEFT JOIN volunteers v      ON v.id = i.logged_by
            LEFT JOIN crm_contacts c    ON c.id = i.contact_id
            LEFT JOIN crm_engagements e ON e.id = i.engagement_id
            WHERE (@organizationId::uuid IS NULL OR i.organization_id = @organizationId OR c.organization_id = @organizationId)
              AND (@contactId::uuid IS NULL OR i.contact_id = @contactId)
              AND (@engagementId::uuid IS NULL OR i.engagement_id = @engagementId)
            ORDER BY i.occurred_at DESC
            LIMIT @limit
            """,
            new { organizationId, contactId, engagementId, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    // --- tasks -------------------------------------------------------------

    public async Task<IReadOnlyList<CrmTaskSummary>> GetTasksAsync(
        CrmTaskStatus? status = null, Guid? engagementId = null, Guid? organizationId = null,
        CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CrmTaskSummary>(new CommandDefinition(
            """
            SELECT t.id, t.title, t.status, t.due_on, t.assigned_to,
                   v.display_name AS assigned_to_name,
                   t.engagement_id,
                   e.title        AS engagement_title,
                   COALESCE(t.organization_id, e.organization_id) AS organization_id,
                   o.name         AS organization_name
            FROM crm_tasks t
            LEFT JOIN volunteers v        ON v.id = t.assigned_to
            LEFT JOIN crm_engagements e   ON e.id = t.engagement_id
            LEFT JOIN crm_organizations o ON o.id = COALESCE(t.organization_id, e.organization_id)
            WHERE NOT t.is_deleted
              AND (@status::int IS NULL OR t.status = @status)
              AND (@engagementId::uuid IS NULL OR t.engagement_id = @engagementId)
              AND (@organizationId::uuid IS NULL OR COALESCE(t.organization_id, e.organization_id) = @organizationId)
            ORDER BY t.status, t.due_on NULLS LAST, t.created_at
            """,
            new { status = (int?)status, engagementId, organizationId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> AddTaskAsync(
        string title, Guid? engagementId, Guid? organizationId, DateOnly? dueOn, Guid? assignedTo,
        CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_tasks (engagement_id, organization_id, title, due_on, assigned_to)
            VALUES (@engagementId, @organizationId, @title, @dueOn, @assignedTo)
            RETURNING id
            """,
            new { engagementId, organizationId, title, dueOn, assignedTo }, cancellationToken: ct));
    }

    public async Task<bool> SetTaskStatusAsync(Guid id, CrmTaskStatus status, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crm_tasks
            SET status = @status, completed_at = CASE WHEN @status = 1 THEN now() ELSE NULL END
            WHERE id = @id AND NOT is_deleted
            """,
            new { id, status = (int)status }, cancellationToken: ct)) > 0;
    }

    /// <summary>
    /// The table name never comes from input — only from the call sites in this
    /// class, one per kind of record — so soft deletion stays one shape.
    /// </summary>
    private async Task<bool> SoftDeleteAsync(string table, Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE {table} SET is_deleted = true, deleted_at = now() WHERE id = @id AND NOT is_deleted",
            new { id }, cancellationToken: ct)) > 0;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
