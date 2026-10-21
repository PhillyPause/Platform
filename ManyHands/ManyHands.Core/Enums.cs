namespace ManyHands.Core;

/// <summary>
/// What a volunteer may do. One organisation, so one role per person; each
/// level includes everything below it.
/// </summary>
public enum VolunteerRole
{
    Member = 0,
    /// <summary>Runs activities: creates and publishes them, keeps the roster, checks people in.</summary>
    Organizer = 1,
    /// <summary>Everything an organizer does, plus deciding who else is one.</summary>
    Admin = 2
}

public enum ActivityKind
{
    OneTime = 0,
    Ongoing = 1
}

public enum SignupStatus
{
    Confirmed = 0,
    Waitlisted = 1,
    Cancelled = 2
}

/// <summary>Where a signup came from. Organizers ask, and it tunes outreach.</summary>
public enum SignupSource
{
    Web = 0,
    Discord = 1,
    Organizer = 2,
    Import = 3
}

public enum EmailStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    /// <summary>Accepted by the logging sender; nothing left the building.</summary>
    Simulated = 3,
    /// <summary>Suppressed before send: unsubscribed, opted out, or flood-capped.</summary>
    Suppressed = 4
}

public enum InviteState
{
    /// <summary>Written, waiting on the dispatcher to mint a link and send it.</summary>
    Queued = 0,
    Sent = 1,
    Accepted = 2,
    Expired = 3,
    Revoked = 4
}
