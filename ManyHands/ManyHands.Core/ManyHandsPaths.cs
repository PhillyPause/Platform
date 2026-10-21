namespace ManyHands.Core;

/// <summary>
/// Every route ManyHands serves, in one place, so the links in an email and the
/// pages that answer them cannot drift apart. Volunteers' pages live under
/// <c>/volunteer</c>; the tools organizers use live under <c>/team</c>.
/// </summary>
public static class ManyHandsPaths
{
    /// <summary>Everything a volunteer has a page for sits under this.</summary>
    public const string Volunteer = "/volunteer";

    /// <summary>
    /// What is coming up, open to anyone: the front page's Events dialog, not
    /// a page of its own. <c>/events</c> loads the front page with it open.
    /// </summary>
    public const string Events = "/events";

    public static string Activity(Guid activityId) => $"/volunteer/activity/{activityId}";

    /// <summary>A volunteer's own signups and interests.</summary>
    public const string Me = "/volunteer/me";

    public const string SignIn = "/volunteer/sign-in";

    /// <summary>A page with one button: the organizers' app sends people here, as signing out must clear a cookie.</summary>
    public const string SignOut = "/volunteer/sign-out";

    /// <summary>
    /// Where a sign-in link lands. The page asks for one click before the token
    /// is spent, because mail scanners open links on the reader's behalf.
    /// </summary>
    public static string RedeemSignIn(string token) => $"/volunteer/sign-in/{token}";

    /// <summary>
    /// Joining is the front page's Join us dialog, not a page of its own:
    /// <c>/join</c> loads the front page with it open, and its form posts here.
    /// </summary>
    public const string Join = "/join";

    public static string AcceptInvite(string token) => $"/volunteer/join/{token}";

    /// <summary>
    /// The link in a joining email that says "this address is mine": the
    /// front page with a dialog whose button, not the visit, confirms, so a
    /// mail scanner cannot confirm anyone.
    /// </summary>
    public static string ConfirmJoin(string token) => $"/volunteer/confirm/{token}";

    /// <summary>
    /// Where confirming lands: the front page with the welcome open, after a
    /// redirect, so the account bar already shows them signed in.
    /// </summary>
    public const string Welcome = "/volunteer/welcome";

    /// <summary>"Not you, or changed your mind?": asks, then deletes the person outright.</summary>
    public static string Remove(Guid commsKey) => $"/volunteer/remove/{commsKey}";

    /// <summary>The link in an email's footer: a page with a button, so a mail scanner cannot unsubscribe anyone.</summary>
    public static string Unsubscribe(Guid commsKey) => $"/volunteer/unsubscribe/{commsKey}";

    /// <summary>The List-Unsubscribe header's target: a mail client's own button POSTs here (RFC 8058).</summary>
    public static string OneClickUnsubscribe(Guid commsKey) => $"/volunteer/unsubscribe/{commsKey}/one-click";

    /// <summary>Where <c>/link</c> in Discord sends someone, to link that account to theirs.</summary>
    public static string LinkDiscord(string token) => $"/volunteer/link-discord/{token}";

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
