using ManyHands.Core;

namespace ManyHands.Data.Email;

/// <summary>
/// Plain-text email bodies. Text only on purpose: it renders everywhere, never
/// lands in a spam folder for HTML reasons, and is honest about what this is —
/// volunteers organizing, not a marketing list.
/// </summary>
public static class EmailTemplates
{
    private const string Org = Organisation.Name;

    public static (string Subject, string Body) MagicLink(
        string displayName, string linkUrl, TimeSpan validFor)
    {
        var body =
            $"""
             Hi {displayName},

             Here is your {Org} sign-in link:

             {linkUrl}

             It works once and expires in {(int)validFor.TotalMinutes} minutes. If you did
             not ask to sign in, ignore this email — nothing has changed.
             """;

        return ($"Your {Org} sign-in link", body);
    }

    public static (string Subject, string Body) SignupConfirmation(
        string displayName,
        string activityTitle,
        DateTimeOffset? startsAt,
        string? location,
        bool waitlisted,
        string manageUrl,
        string unsubscribeUrl)
    {
        var when = startsAt is DateTimeOffset s
            ? LocalTime.Format(s)
            : "Ongoing — the organizers will be in touch about timing";

        var opening = waitlisted
            ? $"You are on the waitlist for {activityTitle}."
            : $"You are signed up for {activityTitle}.";

        var waitlistNote = waitlisted
            ? "\nIf a place opens up, we will email you straight away.\n"
            : string.Empty;

        var body =
            $"""
             Hi {displayName},

             {opening}

             What:  {activityTitle}
             When:  {when}
             Where: {location ?? "To be confirmed"}
             {waitlistNote}
             See or cancel your signups here:
             {manageUrl}

             Thank you for pitching in.

             ---
             Stop receiving these emails: {unsubscribeUrl}
             """;

        return ($"Signed up: {activityTitle}", body);
    }

    /// <summary>The email the public page promises: "we will email you if a place opens."</summary>
    public static (string Subject, string Body) WaitlistPromoted(
        string displayName,
        string activityTitle,
        DateTimeOffset? startsAt,
        string? location,
        string manageUrl,
        string unsubscribeUrl)
    {
        var when = startsAt is DateTimeOffset s
            ? LocalTime.Format(s)
            : "See the activity page for timing";

        var body =
            $"""
             Hi {displayName},

             A place opened up for {activityTitle} — you are in.

             When:  {when}
             Where: {location ?? "To be confirmed"}

             Can no longer make it? Free the place for the next person here:
             {manageUrl}

             ---
             Stop receiving these emails: {unsubscribeUrl}
             """;

        return ($"You are in: {activityTitle}", body);
    }

    public static (string Subject, string Body) ActivityReminder(
        string displayName,
        string activityTitle,
        DateTimeOffset startsAt,
        string? location,
        string? meetingUrl,
        string manageUrl,
        string unsubscribeUrl)
    {
        var joinLine = string.IsNullOrWhiteSpace(meetingUrl)
            ? string.Empty
            : $"\nJoin online: {meetingUrl}\n";

        var body =
            $"""
             Hi {displayName},

             A reminder that you are signed up for {activityTitle}.

             When:  {LocalTime.Format(startsAt)}
             Where: {location ?? "To be confirmed"}
             {joinLine}
             Can no longer make it? Let the organizers know by cancelling here:
             {manageUrl}

             ---
             Stop receiving these emails: {unsubscribeUrl}
             """;

        return ($"Reminder: {activityTitle}", body);
    }

    /// <summary>
    /// Something new that matches what the volunteer said they like doing. One
    /// per activity per person, enforced by the ping claim.
    /// </summary>
    public static (string Subject, string Body) InterestPing(
        string displayName,
        string activityTitle,
        DateTimeOffset? startsAt,
        string? location,
        string activityUrl,
        string unsubscribeUrl)
    {
        var when = startsAt is DateTimeOffset s ? LocalTime.Format(s) : "Ongoing — help whenever you can";

        var body =
            $"""
             Hi {displayName},

             Something new matches what you said you like doing:

             What:  {activityTitle}
             When:  {when}
             Where: {location ?? "To be confirmed"}

             Have a look, and sign up if you can:
             {activityUrl}

             ---
             You get these because of the interests you picked. Change them from
             your page, or stop all email here: {unsubscribeUrl}
             """;

        return ($"New: {activityTitle}", body);
    }

    /// <summary>
    /// An organizer asking somebody to join. It reads like a person wrote it,
    /// because one did: the note is theirs.
    /// </summary>
    public static (string Subject, string Body) Invite(
        string? invitedByName, string? note, string joinUrl, TimeSpan validFor)
    {
        var from = string.IsNullOrWhiteSpace(invitedByName) ? $"The organizers of {Org}" : $"{invitedByName}, from {Org},";
        var personal = string.IsNullOrWhiteSpace(note) ? string.Empty : $"\n{note.Trim()}\n";

        var body =
            $"""
             Hello,

             {from} would like you to join us.
             {personal}
             {Org} is volunteers in Philadelphia working to stop the race to
             build AI nobody can control. You tell us what you are up for —
             tabling, calling Congress, events, writing — and we only tell you
             about those.

             Join here:

             {joinUrl}

             The link works once and expires in {(int)validFor.TotalDays} days. If this is not
             for you, ignore this email; we will not send another.
             """;

        return ($"Join {Org}", body);
    }

    /// <summary>The first thing someone receives when they join: what is on, and where their page is.</summary>
    public static (string Subject, string Body) Welcome(
        string displayName,
        string activitiesUrl,
        string manageUrl,
        string unsubscribeUrl)
    {
        var body =
            $"""
             Hi {displayName},

             Welcome to {Org}. You told us what you like doing, and that is
             all we will email you about — new activities that match.

             See what is coming up:
             {activitiesUrl}

             Your signups and interests live here, changeable any time:
             {manageUrl}

             Glad to have you.

             ---
             Stop receiving these emails: {unsubscribeUrl}
             """;

        return ($"Welcome to {Org}", body);
    }
}
