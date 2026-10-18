namespace ManyHands.Core;

/// <summary>
/// Outcome of a public signup. Expected failures are values, not exceptions.
/// </summary>
public abstract record SignupResult
{
    public sealed record Ok(
        Guid SignupId,
        Guid VolunteerId,
        SignupStatus Status,
        bool AlreadySignedUp) : SignupResult;

    /// <summary>Missing or unpublished.</summary>
    public sealed record ActivityNotFound : SignupResult;

    /// <summary>The activity has already finished.</summary>
    public sealed record ActivityFinished : SignupResult;

    public sealed record InvalidEmail : SignupResult;
}

/// <summary>Outcome of redeeming a sign-in link.</summary>
public abstract record MagicLinkResult
{
    public sealed record Ok(Guid VolunteerId, string DisplayName) : MagicLinkResult;

    /// <summary>Unknown, already used, or expired — deliberately indistinguishable.</summary>
    public sealed record Invalid : MagicLinkResult;
}
