using Dapper;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Email;
using ManyHands.Data.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    /// <summary>The logging sender: records every message in email_messages and sends nothing.</summary>
    public static EmailDispatcher Email(NpgsqlDataSource db) => new(
        new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
        new EmailMessageRepository(db),
        Options.Create(new EmailOptions()),
        NullLogger<EmailDispatcher>.Instance);

    public static OutboxDispatcher Dispatcher(NpgsqlDataSource db, EmailDispatcher? email = null) => new(
        new NotificationQueries(db),
        new VolunteerRepository(db),
        new ActivityRepository(db),
        new ActivityAdminRepository(db),
        new ReportingRepository(db),
        new PingRepository(db),
        new InviteRepository(db),
        email ?? Email(db),
        NullLogger<OutboxDispatcher>.Instance);

    public static OutboxMessage Message(string kind, object payload) =>
        new(0, kind, System.Text.Json.JsonSerializer.Serialize(payload), Attempts: 1);

    /// <summary>Subjects of the mail recorded for an address, oldest first.</summary>
    public static async Task<IReadOnlyList<string>> SubjectsSentToAsync(NpgsqlDataSource db, string address)
    {
        await using var conn = await db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT subject FROM email_messages WHERE lower(to_address) = lower(@address) ORDER BY created_at",
            new { address });
        return rows.AsList();
    }

    /// <summary>A volunteer with an address and these interests.</summary>
    public static async Task<Volunteer> VolunteerAsync(
        NpgsqlDataSource db, string name = "Test Person", VolunteerRole role = VolunteerRole.Member, params short[] interests)
    {
        var volunteers = new VolunteerRepository(db);
        var id = await volunteers.UpsertByEmailAsync(NewEmail(), name);
        if (interests.Length > 0) await volunteers.SetInterestsAsync(id, interests);
        if (role != VolunteerRole.Member) await volunteers.SetRoleAsync(id, role);
        return (await volunteers.GetByIdAsync(id))!;
    }

    public static async Task<int> CountOutboxWelcomesAsync(NpgsqlDataSource db, Guid volunteerId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM outbox WHERE kind = @kind AND payload->>'volunteerId' = @id",
            new { kind = OutboxKinds.MemberWelcome, id = volunteerId.ToString() });
    }

    public static async Task<int> CountOutboxAsync(NpgsqlDataSource db, string kind, Guid signupId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM outbox WHERE kind = @kind AND payload->>'signupId' = @id",
            new { kind, id = signupId.ToString() });
    }
}
