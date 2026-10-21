using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

public sealed record ContactMatch(Guid ContactId, Guid? OrganizationId);

/// <summary>
/// Outreach correspondence, shared by the team. Distinct from
/// EmailMessageRepository, which holds volunteers' own transactional mail and
/// is shown on no page.
/// </summary>
public sealed class CrmEmailRepository(NpgsqlDataSource db)
{
    private const string Columns =
        """
        id, direction, organization_id, contact_id, engagement_id, from_address, to_address,
        subject, body_text, message_id, in_reply_to, conversation_id, status, provider_message_id,
        error, sent_by, occurred_at, created_at, needs_triage
        """;

    public async Task<Guid> RecordAsync(CrmEmail email, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crm_emails
              (direction, organization_id, contact_id, engagement_id, from_address, to_address,
               subject, body_text, message_id, in_reply_to, conversation_id, status,
               provider_message_id, error, sent_by, occurred_at, needs_triage)
            VALUES
              (@Direction, @OrganizationId, @ContactId, @EngagementId, @FromAddress, @ToAddress,
               @Subject, @BodyText, @MessageId, @InReplyTo, @ConversationId, @Status,
               @ProviderMessageId, @Error, @SentBy, @OccurredAt, @NeedsTriage)
            -- A provider can deliver the same inbound message twice; the unique
            -- index on message_id makes the second a no-op, not a duplicate.
            ON CONFLICT (message_id) WHERE message_id IS NOT NULL
            DO UPDATE SET status = EXCLUDED.status
            RETURNING id
            """,
            new
            {
                Direction = (int)email.Direction,
                email.OrganizationId,
                email.ContactId,
                email.EngagementId,
                email.FromAddress,
                email.ToAddress,
                email.Subject,
                email.BodyText,
                email.MessageId,
                email.InReplyTo,
                email.ConversationId,
                Status = (int)email.Status,
                email.ProviderMessageId,
                email.Error,
                email.SentBy,
                OccurredAt = email.OccurredAt.ToUniversalTime(),
                email.NeedsTriage
            },
            cancellationToken: ct));
    }

    public async Task UpdateDeliveryAsync(
        Guid id, EmailStatus status, string? providerMessageId, string? error, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE crm_emails SET status = @status, provider_message_id = @providerMessageId, error = @error WHERE id = @id",
            new { id, status = (int)status, providerMessageId, error }, cancellationToken: ct));
    }

    /// <summary>One row per conversation: the latest message, and whether anything in it still needs a person.</summary>
    public async Task<IReadOnlyList<EmailThreadSummary>> GetThreadsAsync(
        bool triageOnly = false, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<EmailThreadSummary>(new CommandDefinition(
            """
            SELECT * FROM (
              SELECT DISTINCT ON (e.conversation_id)
                     e.conversation_id, e.subject, e.from_address, e.to_address,
                     e.occurred_at AS last_message_at, e.direction,
                     o.name      AS organization_name,
                     c.full_name AS contact_name,
                     (SELECT count(*)::int FROM crm_emails m WHERE m.conversation_id = e.conversation_id) AS message_count,
                     (SELECT bool_or(m.needs_triage) FROM crm_emails m WHERE m.conversation_id = e.conversation_id) AS needs_triage
              FROM crm_emails e
              LEFT JOIN crm_organizations o ON o.id = e.organization_id
              LEFT JOIN crm_contacts c      ON c.id = e.contact_id
              ORDER BY e.conversation_id, e.occurred_at DESC
            ) t
            WHERE NOT @triageOnly OR t.needs_triage
            ORDER BY t.last_message_at DESC
            LIMIT 300
            """,
            new { triageOnly }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CrmEmail>> GetThreadAsync(Guid conversationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CrmEmail>(new CommandDefinition(
            $"SELECT {Columns} FROM crm_emails WHERE conversation_id = @conversationId ORDER BY occurred_at",
            new { conversationId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Outreach mail about one organization, newest first.</summary>
    public async Task<IReadOnlyList<CrmEmail>> GetForOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CrmEmail>(new CommandDefinition(
            $"SELECT {Columns} FROM crm_emails WHERE organization_id = @organizationId ORDER BY occurred_at DESC LIMIT 50",
            new { organizationId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Which thread a reply belongs to. In-Reply-To first: it is the reliable
    /// signal when the provider kept our Message-ID. Failing that, the latest
    /// thread we wrote to this sender in, recently — far better than matching
    /// on subject lines, which would merge unrelated conversations.
    /// </summary>
    public async Task<Guid?> FindConversationAsync(string? inReplyTo, string sender, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        if (!string.IsNullOrWhiteSpace(inReplyTo)
            && await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT conversation_id FROM crm_emails WHERE message_id = @inReplyTo LIMIT 1",
                new { inReplyTo = inReplyTo.Trim() }, cancellationToken: ct)) is Guid replied)
        {
            return replied;
        }

        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT conversation_id FROM crm_emails
            WHERE direction = 0 AND lower(to_address) = lower(@sender)
              AND occurred_at > now() - interval '90 days'
            ORDER BY occurred_at DESC
            LIMIT 1
            """,
            new { sender }, cancellationToken: ct));
    }

    /// <summary>The latest inbound Message-ID in a thread, so a reply threads in the recipient's mail client too.</summary>
    public async Task<string?> GetLastInboundMessageIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT message_id FROM crm_emails
            WHERE conversation_id = @conversationId AND direction = 1 AND message_id IS NOT NULL
            ORDER BY occurred_at DESC LIMIT 1
            """,
            new { conversationId }, cancellationToken: ct));
    }

    /// <summary>Says who an untriaged thread is with, and clears it from the triage queue.</summary>
    public async Task<bool> TriageAsync(
        Guid conversationId, Guid? contactId, Guid? organizationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crm_emails
            SET contact_id      = COALESCE(@contactId, contact_id),
                organization_id = COALESCE(@organizationId, organization_id),
                needs_triage    = false
            WHERE conversation_id = @conversationId
            """,
            new { conversationId, contactId, organizationId }, cancellationToken: ct)) > 0;
    }

    /// <summary>Matches a sender to someone we already know.</summary>
    public async Task<ContactMatch?> FindContactByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ContactMatch>(new CommandDefinition(
            """
            SELECT id AS contact_id, organization_id FROM crm_contacts
            WHERE lower(email) = lower(@email) AND NOT is_deleted
            ORDER BY created_at
            LIMIT 1
            """,
            new { email }, cancellationToken: ct));
    }

    // --- templates ---------------------------------------------------------

    public async Task<IReadOnlyList<CrmEmailTemplate>> GetTemplatesAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CrmEmailTemplate>(new CommandDefinition(
            "SELECT id, name, subject, body_text, created_at FROM crm_email_templates WHERE NOT is_deleted ORDER BY name",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> AddTemplateAsync(string name, string subject, string bodyText, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO crm_email_templates (name, subject, body_text) VALUES (@name, @subject, @bodyText) RETURNING id",
            new { name, subject, bodyText }, cancellationToken: ct));
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE crm_email_templates SET is_deleted = true, deleted_at = now() WHERE id = @id AND NOT is_deleted",
            new { id }, cancellationToken: ct)) > 0;
    }
}
