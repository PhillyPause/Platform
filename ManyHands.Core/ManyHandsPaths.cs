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

    /// <summary>The link in an email's footer: a page with a button, so a mail scanner cannot unsubscribe anyone.</summary>
    public static string Unsubscribe(Guid commsKey) => $"/volunteer/unsubscribe/{commsKey}";

    /// <summary>The List-Unsubscribe header's target: a mail client's own button POSTs here (RFC 8058).</summary>
    public static string OneClickUnsubscribe(Guid commsKey) => $"/volunteer/unsubscribe/{commsKey}/one-click";

    /// <summary>Everything held about the signed-in volunteer, as a file to keep.</summary>
    public const string Export = "/volunteer/me/export.json";

    /// <summary>Every published activity, for any calendar app.</summary>
    public const string Calendar = "/volunteer/calendar.ics";

    /// <summary>One volunteer's own signups. Unguessable rather than signed in.</summary>
    public static string PersonalCalendar(Guid calendarKey) => $"/volunteer/calendar/{calendarKey}.ics";

    /// <summary>A volunteer checking themselves in at the door, from the code an organizer shows.</summary>
    public static string SelfCheckIn(string token) => $"/volunteer/check-in/{token}";

    /// <summary>The organizers' console: every activity, drafts included.</summary>
    public const string Team = "/team";

    public const string TeamNewActivity = "/team/activity/new";

    /// <summary>One activity's roster, door list and publish switch.</summary>
    public static string TeamActivity(Guid activityId) => $"/team/activity/{activityId}";

    public static string TeamEditActivity(Guid activityId) => $"/team/activity/{activityId}/edit";

    /// <summary>The page to put on a screen at the door.</summary>
    public static string TeamCheckInCode(Guid activityId) => $"/team/activity/{activityId}/check-in";

    public const string TeamMembers = "/team/members";

    public const string TeamInvites = "/team/invites";

    /// <summary>The CRM: organizations, and the open tasks.</summary>
    public const string Crm = "/team/crm";

    public static string CrmOrganization(Guid organizationId) => $"/team/crm/organization/{organizationId}";

    public const string CrmPipeline = "/team/crm/pipeline";

    public static string CrmEngagement(Guid engagementId) => $"/team/crm/engagement/{engagementId}";

    public const string CrmOfficials = "/team/crm/officials";

    /// <summary>Outreach mail, every thread.</summary>
    public const string Inbox = "/team/inbox";

    public static string InboxThread(Guid conversationId) => $"/team/inbox/{conversationId}";

    public const string InboxCompose = "/team/inbox/new";

    public const string InboxTemplates = "/team/inbox/templates";
}
