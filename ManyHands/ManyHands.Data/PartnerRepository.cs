using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>What an organizer sets on a partner.</summary>
public sealed record PartnerFields(
    string? Priority, OutreachStatus Status, Audience? Audience, AskRung? AskRung, string? Category,
    string? TrackingCode, string? ContactChannel, string? FoundVia, string? WhyTheyFit);

public enum PartnerSaveResult
{
    Saved,
    NotFound,
    /// <summary>Another live organization already has that tracking code.</summary>
    CodeTaken,
    /// <summary>PhillyPause does not contact this organisation (<see cref="NeverContact"/>).</summary>
    NeverContact
}

/// <summary>What an import did: what went in, what was refused, and why.</summary>
public sealed record PartnerImportReport(
    int Added,
    int Updated,
    int LinksAdded,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> Problems);

/// <summary>
/// Partner outreach on top of the CRM: an organization becomes a partner once
/// it has an outreach status. Signups are counted by tracking code, from
/// <c>volunteers.source</c>, including people who have since deleted
/// themselves: a partner who brought twelve still brought twelve.
/// </summary>
public sealed class PartnerRepository(NpgsqlDataSource db)
{
    // Dapper maps onto constructor parameters by name, never onto value tuples.
    private sealed record IdRow(Guid Id);
    private sealed record NameRow(string Name, string? Website);

    public async Task<IReadOnlyList<PartnerSummary>> GetPartnersAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PartnerSummary>(new CommandDefinition(
            """
            SELECT o.id, o.name, o.priority, o.outreach_status, o.audience, o.category, o.tracking_code,
                   COALESCE(m.members, 0)   AS members,
                   COALESCE(m.confirmed, 0) AS confirmed_members,
                   o.first_contacted_at,
                   (SELECT max(i.occurred_at) FROM crm_interactions i
                     WHERE i.organization_id = o.id)                           AS last_touch_at
            FROM crm_organizations o
            LEFT JOIN LATERAL (
                SELECT count(*)::int AS members,
                       (count(*) FILTER (WHERE v.email_verified_at IS NOT NULL))::int AS confirmed
                FROM volunteers v
                WHERE v.source = o.tracking_code
            ) m ON o.tracking_code IS NOT NULL
            WHERE NOT o.is_deleted AND o.outreach_status IS NOT NULL
            ORDER BY o.priority NULLS LAST, lower(o.name)
            """,
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PartnerDetails?> GetPartnerAsync(Guid organizationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PartnerDetails>(new CommandDefinition(
            """
            SELECT id, priority, outreach_status, audience, ask_rung, category, tracking_code,
                   contact_channel, found_via, why_they_fit, first_contacted_at, followed_up_at, replied_at
            FROM crm_organizations WHERE id = @organizationId AND NOT is_deleted
            """,
            new { organizationId }, cancellationToken: ct));
    }

    /// <summary>Makes an organization a partner, or changes one. A status set by hand stays as set.</summary>
    public async Task<PartnerSaveResult> SavePartnerAsync(
        Guid organizationId, PartnerFields fields, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var org = await conn.QuerySingleOrDefaultAsync<NameRow>(new CommandDefinition(
            "SELECT name, website FROM crm_organizations WHERE id = @organizationId AND NOT is_deleted",
            new { organizationId }, cancellationToken: ct));
        if (org is null) return PartnerSaveResult.NotFound;
        if (NeverContact.Matches(org.Name, org.Website)) return PartnerSaveResult.NeverContact;

        var code = TrackingCode.Normalize(fields.TrackingCode);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE crm_organizations
                SET priority = @priority, outreach_status = @status, audience = @audience, ask_rung = @rung,
                    category = @category, tracking_code = @code, contact_channel = @channel,
                    found_via = @foundVia, why_they_fit = @why
                WHERE id = @organizationId AND NOT is_deleted
                """,
                new
                {
                    organizationId,
                    priority = PartnerSheet.Priority(fields.Priority),
                    status = (int)fields.Status,
                    audience = (int?)fields.Audience,
                    rung = (int?)fields.AskRung,
                    category = Blank(fields.Category),
                    code,
                    channel = Blank(fields.ContactChannel),
                    foundVia = Blank(fields.FoundVia),
                    why = Blank(fields.WhyTheyFit)
                }, cancellationToken: ct));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return PartnerSaveResult.CodeTaken;
        }

        return PartnerSaveResult.Saved;
    }

    public async Task<IReadOnlyList<OrganizationLink>> GetLinksAsync(Guid organizationId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OrganizationLink>(new CommandDefinition(
            """
            SELECT l.id, true AS outgoing, l.connection,
                   l.to_organization_id AS other_id, COALESCE(t.name, l.to_name) AS other_name, l.source_url
            FROM crm_organization_links l
            LEFT JOIN crm_organizations t ON t.id = l.to_organization_id
            WHERE l.from_organization_id = @organizationId AND NOT l.is_deleted
            UNION ALL
            SELECT l.id, false, l.connection,
                   l.from_organization_id, COALESCE(f.name, l.from_name), l.source_url
            FROM crm_organization_links l
            LEFT JOIN crm_organizations f ON f.id = l.from_organization_id
            WHERE l.to_organization_id = @organizationId AND NOT l.is_deleted
            ORDER BY other_name
            """,
            new { organizationId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Who joined through a code, newest first. Someone deleted since is left out by name, not by count.</summary>
    public async Task<IReadOnlyList<ReferredMember>> GetReferredAsync(string trackingCode, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ReferredMember>(new CommandDefinition(
            """
            SELECT id, display_name, created_at AS joined_at, email_verified_at IS NOT NULL AS confirmed
            FROM volunteers
            WHERE source = @trackingCode AND NOT is_deleted
            ORDER BY created_at DESC
            """,
            new { trackingCode }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Confirmed members, split by what brought them: a partner's code, another code, or none.</summary>
    public async Task<MemberGoal> GetGoalAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<MemberGoal>(new CommandDefinition(
            """
            WITH confirmed AS (
                SELECT v.source,
                       EXISTS (SELECT 1 FROM crm_organizations o
                                WHERE o.tracking_code = v.source AND NOT o.is_deleted) AS partner
                FROM volunteers v
                WHERE v.email_verified_at IS NOT NULL AND NOT v.is_deleted
            )
            SELECT count(*)::int                                                    AS confirmed,
                   (count(*) FILTER (WHERE partner))::int                          AS via_partners,
                   (count(*) FILTER (WHERE source IS NOT NULL AND NOT partner))::int AS via_other_codes,
                   (count(*) FILTER (WHERE source IS NULL))::int                   AS no_code
            FROM confirmed
            """,
            cancellationToken: ct));
    }

    // --- import ------------------------------------------------------------

    /// <summary>
    /// The Partner Tracker sheet, pasted or saved as CSV: its Partners tab and
    /// its Org links tab, each with its header row. Safe to run again: an
    /// organization is found by name, and what an organizer has already set on
    /// it is kept. Organisations on <see cref="NeverContact"/> are refused.
    /// </summary>
    public async Task<PartnerImportReport> ImportAsync(
        string? partnersText, string? linksText, Guid? importedBy, CancellationToken ct = default)
    {
        var refused = new List<string>();
        var problems = new List<string>();
        int added = 0, updated = 0, linksAdded = 0;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var partners = DelimitedText.Parse(partnersText);
        if (partners.Count > 0)
        {
            var columns = Columns(partners[0]);
            if (!columns.ContainsKey("organization"))
            {
                problems.Add("The partners' first row needs an Organization heading.");
            }
            else
            {
                foreach (var row in partners.Skip(1))
                {
                    string? Cell(string heading) =>
                        columns.TryGetValue(heading, out var i) && i < row.Length ? Blank(row[i]) : null;

                    var name = Cell("organization");
                    if (name is null) continue;

                    var link = Cell("link");
                    if (NeverContact.Matches(name, link))
                    {
                        refused.Add(name);
                        continue;
                    }

                    var category = Cell("category");
                    var existing = await FindOrganizationAsync(conn, tx, name, ct);
                    var code = PartnerSheet.Code(Cell("tracking code"));
                    if (code is not null && await CodeHolderAsync(conn, tx, code, ct) is Guid holder && holder != existing)
                    {
                        problems.Add($"{name}: the code {code} belongs to another organization, so it was left off.");
                        code = null;
                    }

                    var values = new
                    {
                        id = existing,
                        kind = (int)PartnerSheet.KindFor(category),
                        name,
                        notes = Cell("notes"),
                        link,
                        importedBy,
                        priority = PartnerSheet.Priority(Cell("priority")),
                        status = (int)(PartnerSheet.Status(Cell("status")) ?? OutreachStatus.NotContacted),
                        audience = (int)PartnerSheet.AudienceFor(category),
                        category,
                        code,
                        channel = Cell("contact channel"),
                        foundVia = Cell("found via"),
                        why = Cell("why they fit")
                    };

                    if (existing is null)
                    {
                        await conn.ExecuteAsync(new CommandDefinition(
                            """
                            INSERT INTO crm_organizations
                                (kind, name, description, website, created_by, priority, outreach_status, audience,
                                 category, tracking_code, contact_channel, found_via, why_they_fit)
                            VALUES (@kind, @name, @notes, @link, @importedBy, @priority, @status, @audience,
                                    @category, @code, @channel, @foundVia, @why)
                            """,
                            values, tx, cancellationToken: ct));
                        added++;
                    }
                    else
                    {
                        // What an organizer has set wins over the sheet: only blanks are filled.
                        await conn.ExecuteAsync(new CommandDefinition(
                            """
                            UPDATE crm_organizations
                            SET description     = COALESCE(description, @notes),
                                website         = COALESCE(website, @link),
                                priority        = COALESCE(priority, @priority),
                                outreach_status = COALESCE(outreach_status, @status),
                                audience        = COALESCE(audience, @audience),
                                category        = COALESCE(category, @category),
                                tracking_code   = COALESCE(tracking_code, @code),
                                contact_channel = COALESCE(contact_channel, @channel),
                                found_via       = COALESCE(found_via, @foundVia),
                                why_they_fit    = COALESCE(why_they_fit, @why)
                            WHERE id = @id
                            """,
                            values, tx, cancellationToken: ct));
                        updated++;
                    }
                }
            }
        }

        var links = DelimitedText.Parse(linksText);
        if (links.Count > 0)
        {
            var columns = Columns(links[0]);
            if (!columns.ContainsKey("from") || !columns.ContainsKey("to") || !columns.ContainsKey("connection"))
            {
                problems.Add("The links' first row needs From, Connection and To headings.");
            }
            else
            {
                foreach (var row in links.Skip(1))
                {
                    string? Cell(string heading) =>
                        columns.TryGetValue(heading, out var i) && i < row.Length ? Blank(row[i]) : null;

                    var from = Cell("from");
                    var to = Cell("to");
                    var connection = Cell("connection");
                    if (from is null || to is null || connection is null) continue;
                    if (NeverContact.Matches(from) || NeverContact.Matches(to)) continue;

                    var fromId = await FindOrganizationAsync(conn, tx, from, ct);
                    var toId = await FindOrganizationAsync(conn, tx, to, ct);
                    if (fromId is null && toId is null)
                    {
                        problems.Add($"Link left out, neither end is an organization here: {from} {connection} {to}.");
                        continue;
                    }

                    linksAdded += await conn.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO crm_organization_links
                            (from_organization_id, from_name, connection, to_organization_id, to_name, source_url)
                        SELECT @fromId, @fromName, @connection, @toId, @toName, @source
                        WHERE NOT EXISTS (
                            SELECT 1 FROM crm_organization_links l
                            WHERE NOT l.is_deleted
                              AND l.from_organization_id IS NOT DISTINCT FROM @fromId
                              AND l.from_name IS NOT DISTINCT FROM @fromName
                              AND lower(l.connection) = lower(@connection)
                              AND l.to_organization_id IS NOT DISTINCT FROM @toId
                              AND l.to_name IS NOT DISTINCT FROM @toName)
                        """,
                        new
                        {
                            fromId,
                            fromName = fromId is null ? from : null,
                            connection,
                            toId,
                            toName = toId is null ? to : null,
                            source = Cell("source")
                        }, tx, cancellationToken: ct));
                }
            }
        }

        await tx.CommitAsync(ct);
        return new PartnerImportReport(added, updated, linksAdded, refused, problems);
    }

    private static Dictionary<string, int> Columns(string[] header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            // "Found via (org link)" is "found via": what is in brackets explains the heading.
            var heading = header[i];
            var bracket = heading.IndexOf('(', StringComparison.Ordinal);
            if (bracket > 0) heading = heading[..bracket];
            columns.TryAdd(heading.Trim().ToLowerInvariant(), i);
        }
        return columns;
    }

    private static async Task<Guid?> FindOrganizationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name, CancellationToken ct) =>
        (await conn.QueryFirstOrDefaultAsync<IdRow>(new CommandDefinition(
            """
            SELECT id FROM crm_organizations
            WHERE lower(name) = lower(@name) AND NOT is_deleted
            ORDER BY created_at LIMIT 1
            """,
            new { name }, tx, cancellationToken: ct)))?.Id;

    private static async Task<Guid?> CodeHolderAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string code, CancellationToken ct) =>
        (await conn.QueryFirstOrDefaultAsync<IdRow>(new CommandDefinition(
            "SELECT id FROM crm_organizations WHERE tracking_code = @code AND NOT is_deleted",
            new { code }, tx, cancellationToken: ct)))?.Id;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
