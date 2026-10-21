using System.Text;
using ManyHands.Core;

namespace ManyHands.Data.Discord;

/// <summary>
/// Every string the bot posts, composed here and nowhere else. Pure functions,
/// so the awkward parts — the 2000-character ceiling, and what is sacrificed
/// when a message is too long — are testable without Discord.
/// </summary>
public static class DiscordMessages
{
    /// <summary>
    /// Discord rejects a message over 2000 characters. Measured in UTF-16
    /// units, so an emoji counts double here and once to Discord: we can trim a
    /// shade early, and never overshoot.
    /// </summary>
    public const int MaxMessageLength = 2000;

    /// <summary>Discord's ceiling for a thread name.</summary>
    public const int MaxThreadNameLength = 100;

    private const string Ellipsis = "…";

    /// <summary>Room kept back for the "and N more" line on a list.</summary>
    private const int MoreFooterAllowance = 32;

    /// <summary>Trims to fit, preferring a line break or a space so the last word is whole.</summary>
    public static string Fit(string message, int max = MaxMessageLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 8);
        if (string.IsNullOrEmpty(message) || message.Length <= max) return message;

        var budget = max - Ellipsis.Length;

        // Only a boundary in the back half counts; a break near the start would
        // lose most of the message to a tidy cut.
        var floor = budget / 2;
        var cut = message.LastIndexOf('\n', budget - 1);
        if (cut < floor) cut = message.LastIndexOf(' ', budget - 1);
        if (cut < floor) cut = budget;

        // Never split a surrogate pair.
        if (cut > 0 && char.IsHighSurrogate(message[cut - 1])) cut--;

        return message[..cut].TrimEnd() + Ellipsis;
    }

    /// <summary>
    /// Head and tail always survive; the body absorbs the cut. The sign-up link
    /// lives in the tail because it is the point of the message.
    /// </summary>
    public static string Compose(string head, string? body, string tail, int max = MaxMessageLength)
    {
        if (string.IsNullOrWhiteSpace(body)) return Fit(head + tail, max);

        var budget = max - head.Length - tail.Length - 2;

        // Not enough room for prose worth reading: drop it rather than print
        // three words and an ellipsis.
        if (budget < 40) return Fit(head + tail, max);

        return head + "\n\n" + Fit(body.Trim(), budget) + tail;
    }

    /// <summary>A header and as many lines as fit, with an honest count of what was left out.</summary>
    public static string List(string header, IReadOnlyList<string> items, string emptyText, int max = MaxMessageLength)
    {
        if (items.Count == 0) return Fit(emptyText, max);

        var text = new StringBuilder(header);
        var included = 0;

        foreach (var item in items)
        {
            if (text.Length + item.Length + 1 > max - MoreFooterAllowance) break;
            text.Append('\n').Append(item);
            included++;
        }

        if (included < items.Count)
            text.Append('\n').Append(Ellipsis).Append("and ").Append(items.Count - included).Append(" more.");

        return Fit(text.ToString(), max);
    }

    /// <summary>
    /// Discord renders <c>&lt;t:seconds:F&gt;</c> in each reader's own
    /// timezone, so a time here never needs a label.
    /// </summary>
    public static string When(DateTimeOffset? startsAt) =>
        startsAt is DateTimeOffset s ? $"<t:{s.ToUnixTimeSeconds()}:F>" : "Ongoing — whenever you can";

    /// <summary>The channel post for a newly published activity.</summary>
    public static string Announcement(ActivityListItem activity, string baseUrl)
    {
        var tail = $"\n\nSign up: {baseUrl}{ManyHandsPaths.Activity(activity.Id)}";
        return Compose(Details($"**{activity.Title}**", activity), activity.Description, tail);
    }

    /// <summary>The thread opened on an announcement: just the title, since the post above carries the rest.</summary>
    public static string ThreadName(ActivityListItem activity) =>
        string.IsNullOrWhiteSpace(activity.Title) ? "Activity discussion" : Fit(activity.Title.Trim(), MaxThreadNameLength);

    /// <summary>The link back to an announcement, when we know where it landed.</summary>
    public static string? MessageUrl(ulong guildId, ulong? channelId, ulong? messageId) =>
        channelId is ulong channel && messageId is ulong message
            ? $"https://discord.com/channels/{guildId}/{channel}/{message}"
            : null;

    /// <summary>
    /// The DM to someone whose interests match a new activity. It leads with
    /// why they are hearing from us — an unexplained DM from a bot reads as
    /// spam — and ends with how to stop, this channel's unsubscribe footer.
    /// </summary>
    public static string InterestPing(ActivityListItem activity, string baseUrl)
    {
        var tail =
            $"\n\nSign up: {baseUrl}{ManyHandsPaths.Activity(activity.Id)}" +
            $"\nChange what you get pinged about: {baseUrl}{ManyHandsPaths.Me}";
        return Compose(Details($"New, and it matches what you said you like doing:\n**{activity.Title}**", activity),
            activity.Description, tail);
    }

    /// <summary>
    /// The note in an activity's thread when someone signs up. It names nobody:
    /// a thread is public, and who volunteered for what is not the server's
    /// business. It keeps the thread visibly filling up, which is what makes
    /// people click.
    /// </summary>
    public static string ThreadSignupNote(SignupStatus status) =>
        status == SignupStatus.Waitlisted
            ? "Someone just joined the waitlist for this one."
            : "Someone just signed up for this one.";

    /// <summary>The DM beside a confirmation email: a nudge, with the detail one click away.</summary>
    public static string SignupConfirmation(SignupNotification signup, string baseUrl)
    {
        var head = new StringBuilder(signup.Status == SignupStatus.Waitlisted
                ? $"You are on the waitlist for **{signup.Title}**."
                : $"You are signed up for **{signup.Title}**.")
            .Append('\n').Append(When(signup.StartsAt));

        if (!string.IsNullOrWhiteSpace(signup.Location)) head.Append("\nWhere: ").Append(signup.Location);

        return Compose(head.ToString(), body: null, $"\n\nSee or cancel your signups: {baseUrl}{ManyHandsPaths.Me}");
    }

    /// <summary>One line of the <c>/activities</c> reply.</summary>
    public static string ActivityLine(ActivityListItem activity, string baseUrl)
    {
        var line = new StringBuilder("• **").Append(activity.Title).Append("** — ").Append(When(activity.StartsAt));
        if (!string.IsNullOrWhiteSpace(activity.Location)) line.Append(" · ").Append(activity.Location);
        if (activity.IsFull) line.Append(" · waitlist only");
        else if (activity.SpotsRemaining is int spots) line.Append(" · ").Append(spots).Append(" places left");
        return line.Append("\n  ").Append(baseUrl).Append(ManyHandsPaths.Activity(activity.Id)).ToString();
    }

    /// <summary>One line of the <c>/mysignups</c> reply.</summary>
    public static string SignupLine(MyActivity signup, string baseUrl)
    {
        var line = new StringBuilder("• **").Append(signup.Title).Append("** — ").Append(When(signup.StartsAt));
        if (signup.Status == SignupStatus.Waitlisted) line.Append(" · waitlisted");
        if (!string.IsNullOrWhiteSpace(signup.Location)) line.Append(" · ").Append(signup.Location);
        return line.Append("\n  ").Append(baseUrl).Append(ManyHandsPaths.Activity(signup.ActivityId)).ToString();
    }

    /// <summary>When a Discord account has no volunteer behind it: the one thing that fixes it.</summary>
    public const string NotLinked =
        "Your Discord account is not linked to PhillyPause yet. Type `/link` and follow the link it gives you.";

    /// <summary>The reply to <c>/link</c>, seen only by the person who asked.</summary>
    public static string LinkInstructions(string linkUrl, TimeSpan validFor) =>
        $"Open this to link your Discord account to PhillyPause. Sign in there if you are asked to, then press the button.\n" +
        $"{linkUrl}\nIt works for {(int)validFor.TotalMinutes} minutes, and only for you: do not share it.";

    private static string Details(string opening, ActivityListItem activity)
    {
        var head = new StringBuilder(opening).Append('\n').Append(When(activity.StartsAt));
        if (!string.IsNullOrWhiteSpace(activity.Location)) head.Append("\nWhere: ").Append(activity.Location);
        if (!string.IsNullOrWhiteSpace(activity.MeetingUrl)) head.Append("\nJoin online: ").Append(activity.MeetingUrl);
        if (activity.Tags.Count > 0) head.Append('\n').Append(string.Join(" · ", activity.Tags));
        return head.ToString();
    }
}
