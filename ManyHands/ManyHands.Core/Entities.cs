namespace ManyHands.Core;

// Persistence-shaped records. Dapper maps snake_case columns onto these by
// constructor parameter name (DefaultTypeMap.MatchNamesWithUnderscores, set in
// ManyHands.Data), and by exact type: an array column has to be string[], not
// IReadOnlyList<string>.

public sealed record Volunteer(
    Guid Id,
    string DisplayName,
    string? Email,
    ulong? DiscordUserId,
    VolunteerRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EmailVerifiedAt,
    bool EmailOptIn,
    DateTimeOffset? UnsubscribedAt,
    Guid CommsKey,
    // A separate secret from CommsKey: revoking a leaked calendar feed must
    // not also break the person's unsubscribe links.
    Guid CalendarKey,
    bool IsDeleted)
{
    /// <summary>
    /// Opted in and never unsubscribed. The consent is one consent: a Discord DM
    /// is a different pipe, not a different permission.
    /// </summary>
    public bool MayContact => EmailOptIn && UnsubscribedAt is null && !IsDeleted;

    /// <summary>Mail may only be sent when opted in and never unsubscribed.</summary>
    public bool CanReceiveEmail => MayContact && !string.IsNullOrWhiteSpace(Email);

    public bool IsAtLeast(VolunteerRole role) => !IsDeleted && Role >= role;
}

/// <summary>One entry in the interest vocabulary.</summary>
public sealed record ActivityType(
    short Id,
    string Slug,
    string Label,
    string? Description,
    short SortOrder,
    bool IsActive);

/// <summary>An activity plus the bits a public page renders.</summary>
public sealed record ActivityListItem(
    Guid Id,
    string Title,
    string? Description,
    ActivityKind Kind,
    string? Location,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? MeetingUrl,
    int SignupCount,
    int? Capacity,
    /// <summary>The activity-type labels this is tagged with.</summary>
    IReadOnlyList<string> Tags)
{
    public bool IsFull => Capacity is int cap && SignupCount >= cap;

    public int? SpotsRemaining => Capacity is int cap ? Math.Max(0, cap - SignupCount) : null;
}

/// <summary>One row on the members page.</summary>
public sealed record Member(
    Guid Id,
    string DisplayName,
    string? Email,
    VolunteerRole Role,
    DateTimeOffset CreatedAt,
    bool CanEmail,
    string[] Interests);

/// <summary>What a volunteer sees on their own page.</summary>
public sealed record MyActivity(
    Guid SignupId,
    Guid ActivityId,
    string Title,
    string? Location,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    SignupStatus Status);
