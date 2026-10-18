namespace ManyHands.Core;

/// <summary>
/// Every route ManyHands serves, in one place, so the links in an email and the
/// pages that answer them cannot drift apart. Volunteers' pages live under
/// <c>/volunteer</c>; the tools organizers use live under <c>/team</c>.
/// </summary>
public static class ManyHandsPaths
{
    /// <summary>What is coming up, open to anyone.</summary>
    public const string Activities = "/volunteer";

    public static string Activity(Guid activityId) => $"/volunteer/activity/{activityId}";

    /// <summary>A volunteer's own signups and interests.</summary>
    public const string Me = "/volunteer/me";

    public const string SignIn = "/volunteer/sign-in";

    /// <summary>
    /// Where a sign-in link lands. The page asks for one click before the token
    /// is spent, because mail scanners open links on the reader's behalf.
    /// </summary>
    public static string RedeemSignIn(string token) => $"/volunteer/sign-in/{token}";

    public const string Join = "/volunteer/join";

    public static string AcceptInvite(string token) => $"/volunteer/join/{token}";

    public static string Unsubscribe(Guid commsKey) => $"/volunteer/unsubscribe/{commsKey}";

    /// <summary>Every published activity, for any calendar app.</summary>
    public const string Calendar = "/volunteer/calendar.ics";

    /// <summary>One volunteer's own signups. Unguessable rather than signed in.</summary>
    public static string PersonalCalendar(Guid calendarKey) => $"/volunteer/calendar/{calendarKey}.ics";

    /// <summary>The organizers' console.</summary>
    public const string Team = "/team";
}
