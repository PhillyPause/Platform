using Dapper;
using Npgsql;

namespace ManyHands.Tests;

/// <summary>
/// Arranging rows directly, so a test states exactly the world it needs. Every
/// address is on <c>example.test</c>, which can never receive mail.
/// </summary>
internal static class TestData
{
    public const short Tabling = 1;
    public const short Flyering = 2;
    public const short Calling = 3;
    public const string FlyeringLabel = "Flyering";

    public static string NewEmail() => $"vol-{Guid.NewGuid():N}@example.test";

    /// <summary>
    /// A published activity starting <paramref name="startsInHours"/> from now
    /// and lasting two hours. Null start means ongoing work with no time at all.
    /// </summary>
    public static async Task<Guid> ActivityAsync(
        NpgsqlDataSource db,
        string title = "Saturday flyering",
        double? startsInHours = 72,
        int? capacity = null,
        bool published = true,
        params short[] tags)
    {
        await using var conn = await db.OpenConnectionAsync();

        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO activities (title, kind, location, starts_at, ends_at, capacity, is_published)
            VALUES (@title,
                    CASE WHEN @hours::float8 IS NULL THEN 1 ELSE 0 END,
                    'Clark Park',
                    now() + make_interval(secs => @hours::float8 * 3600),
                    now() + make_interval(secs => @hours::float8 * 3600 + 7200),
                    @capacity, @published)
            RETURNING id
            """,
            new { title, hours = startsInHours, capacity, published });

        foreach (var tag in tags)
        {
            await conn.ExecuteAsync(
                "INSERT INTO activity_tags (activity_id, activity_type_id) VALUES (@id, @tag)",
                new { id, tag });
        }

        return id;
    }

    public static async Task<int> CountOutboxAsync(NpgsqlDataSource db, string kind, Guid signupId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM outbox WHERE kind = @kind AND payload->>'signupId' = @id",
            new { kind, id = signupId.ToString() });
    }
}
