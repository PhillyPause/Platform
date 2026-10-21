using Dapper;
using ManyHands.Core;
using Npgsql;

namespace ManyHands.Data;

/// <summary>The interest vocabulary volunteers pick from and activities are tagged with.</summary>
public sealed class ActivityTypeRepository(NpgsqlDataSource db)
{
    public async Task<IReadOnlyList<ActivityType>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ActivityType>(new CommandDefinition(
            """
            SELECT id, slug, label, description, sort_order, is_active
            FROM activity_types
            WHERE is_active
            ORDER BY sort_order, label
            """,
            cancellationToken: ct));
        return rows.AsList();
    }
}
