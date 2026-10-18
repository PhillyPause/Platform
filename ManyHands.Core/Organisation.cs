namespace ManyHands.Core;

/// <summary>
/// The one organisation ManyHands serves. Constants rather than configuration:
/// there is no second organisation to configure for.
/// </summary>
public static class Organisation
{
    public const string Name = "PhillyPause";

    /// <summary>
    /// Every time a volunteer reads is Philadelphia's wall clock. Stored
    /// instants stay UTC; this is only how they are written down.
    /// </summary>
    public const string TimeZone = "America/New_York";
}
