namespace ManyHands.Core;

/// <summary>How someone reached an official. Stored as its number.</summary>
public enum OutreachChannel
{
    Email = 0,
    Call = 1,
    ContactForm = 2,
    Letter = 3,
    Meeting = 4,
}

/// <summary>What came of it, when the person says. Stored as its number.</summary>
public enum OutreachOutcome
{
    Sent = 0,
    SpokeToSomeone = 1,
    LeftVoicemail = 2,
    NoAnswer = 3,
}

/// <summary>One time someone contacted an official, as the outreach log holds it.</summary>
public sealed record OutreachAction(
    Guid Id,
    Guid? VolunteerId,
    /// <summary>Null for an anonymous action, and "Deleted volunteer" after deletion.</summary>
    string? VolunteerName,
    string OfficialKey,
    string OfficialName,
    OutreachChannel Channel,
    OutreachOutcome? Outcome,
    string? Note,
    DateTimeOffset OccurredAt);

/// <summary>The headline numbers for the organizers' outreach page.</summary>
public sealed record OutreachTotals(
    int ThisWeek,
    int Last30Days,
    int AllTime,
    int PeopleLast30Days,
    int OfficialsLast30Days,
    int AnonymousLast30Days);

/// <summary>One official's line: how often, how, and when last.</summary>
public sealed record OutreachByOfficial(
    string OfficialKey,
    string OfficialName,
    int Actions,
    int Calls,
    int Emails,
    int People,
    DateTimeOffset LastAt);

/// <summary>One person's line on the organizers' page.</summary>
public sealed record OutreachByVolunteer(Guid VolunteerId, string VolunteerName, int Actions, int Officials, DateTimeOffset LastAt);

public static class OutreachText
{
    public static string Label(this OutreachChannel channel) => channel switch
    {
        OutreachChannel.Email => "Email",
        OutreachChannel.Call => "Call",
        OutreachChannel.ContactForm => "Contact form",
        OutreachChannel.Letter => "Letter",
        OutreachChannel.Meeting => "Meeting",
        _ => channel.ToString()
    };

    public static string Label(this OutreachOutcome outcome) => outcome switch
    {
        OutreachOutcome.Sent => "Sent",
        OutreachOutcome.SpokeToSomeone => "Spoke to someone",
        OutreachOutcome.LeftVoicemail => "Left a voicemail",
        OutreachOutcome.NoAnswer => "No answer",
        _ => outcome.ToString()
    };
}
