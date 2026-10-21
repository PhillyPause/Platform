namespace ManyHands.Data;

/// <summary>
/// The outbox discriminators. Renaming one means migrating the stored rows
/// too: the reminder sweep decides "already reminded?" by looking for a row of
/// that kind, so a code-only rename would email everybody again.
/// </summary>
public static class OutboxKinds
{
    /// <summary>Payload <c>signupId</c>. The confirmation email.</summary>
    public const string SignupConfirmed = "signup.confirmed";

    /// <summary>Payload <c>signupId</c>. Off the waitlist: "you are in".</summary>
    public const string SignupPromoted = "signup.promoted";

    /// <summary>Payload <c>signupId</c>. The day-before reminder.</summary>
    public const string ActivityReminder = "activity.reminder";

    /// <summary>Payload <c>volunteerId</c>. One welcome, the first time someone joins.</summary>
    public const string MemberWelcome = "member.welcome";

    /// <summary>Payload <c>activityId</c>. Tells the people whose interests match.</summary>
    public const string ActivityPublished = "activity.published";

    /// <summary>Payload <c>inviteId</c>. Mints the invite link and emails it.</summary>
    public const string InviteSent = "invite.sent";
}
