using Dapper;
using Npgsql;

namespace ManyHands.Data;

public abstract record SelfCheckInResult
{
    public sealed record Ok(string DisplayName, bool AlreadyCheckedIn) : SelfCheckInResult;

    /// <summary>No signup for that address on this activity.</summary>
    public sealed record NotSignedUp : SelfCheckInResult;

    public sealed record InvalidEmail : SelfCheckInResult;
}

/// <summary>
/// Checking yourself in at the door from the QR code. The page has already
/// checked the code; this only matches the address to a signup.
/// </summary>
public sealed class SelfCheckInRepository(NpgsqlDataSource db)
{
    private sealed record MatchedSignup(Guid Id, string DisplayName, DateTimeOffset? CheckedInAt);

    public async Task<SelfCheckInResult> CheckInAsync(Guid activityId, string email, CancellationToken ct = default)
    {
        var address = email.Trim();
        if (!EmailAddress.LooksValid(address)) return new SelfCheckInResult.InvalidEmail();

        await using var conn = await db.OpenConnectionAsync(ct);

        var signup = await conn.QuerySingleOrDefaultAsync<MatchedSignup>(new CommandDefinition(
            """
            SELECT g.id, v.display_name, g.checked_in_at
            FROM signups g
            JOIN volunteers v ON v.id = g.volunteer_id
            WHERE g.activity_id = @activityId AND lower(v.email) = lower(@address) AND g.status <> 2
            """,
            new { activityId, address }, cancellationToken: ct));

        if (signup is null) return new SelfCheckInResult.NotSignedUp();

        if (signup.CheckedInAt is not null)
            return new SelfCheckInResult.Ok(signup.DisplayName, AlreadyCheckedIn: true);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE signups SET checked_in_at = now() WHERE id = @id AND checked_in_at IS NULL",
            new { id = signup.Id }, cancellationToken: ct));

        return new SelfCheckInResult.Ok(signup.DisplayName, AlreadyCheckedIn: false);
    }
}
