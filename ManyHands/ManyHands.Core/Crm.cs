namespace ManyHands.Core;

// The CRM: the people and offices outside PhillyPause we are working on, what
// we have said to them, and what is still to do. An organization carries a
// kind, so congressional outreach, venue booking and coalition work all run on
// the same timelines and pipeline. Shared by the whole team on purpose: when
// somebody steps back, the relationship does not leave with them.

/// <summary>What sort of organization this is. Lobbying is the first use of the CRM, not the only one.</summary>
public enum OrganizationKind
{
    CongressionalOffice = 0,
    StateOrLocalOffice = 1,
    PartnerOrg = 2,
    Venue = 3,
    MediaOutlet = 4,
    CampusGroup = 5,
    Other = 6
}

/// <summary>Where an effort stands: the pipeline every engagement moves through.</summary>
public enum EngagementStatus
{
    Draft = 0,
    Outreach = 1,
    Scheduling = 2,
    Confirmed = 3,
    Completed = 4,
    FollowUp = 5,
    Declined = 6
}

/// <summary>
/// Recorded from the start because it can have tax and reporting consequences;
/// whoever handles compliance decides what belongs where. Not legal advice.
/// </summary>
public enum ActivityCategory
{
    DirectLobbying = 0,
    Grassroots = 1,
    GeneralAdvocacy = 2,
    Education = 3
}

public enum InteractionKind
{
    Note = 0,
    Call = 1,
    Email = 2,
    Meeting = 3,
    Letter = 4,
    SocialMedia = 5
}

public enum CrmTaskStatus
{
    Open = 0,
    Done = 1,
    Cancelled = 2
}

public enum EmailDirection
{
    Outbound = 0,
    Inbound = 1
}

public sealed record CrmOrganization(
    Guid Id,
    OrganizationKind Kind,
    string Name,
    string? Description,
    string? Website,
    string? Address,
    string? State,
    string? District,
    string? ExternalRef,
    DateTimeOffset CreatedAt);

public sealed record CrmContact(
    Guid Id,
    Guid? OrganizationId,
    string FullName,
    string? RoleTitle,
    string? Email,
    string? Phone,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CrmEngagement(
    Guid Id,
    Guid? OrganizationId,
    string Title,
    EngagementStatus Status,
    ActivityCategory? ActivityCategory,
    Guid? OwnerId,
    DateTimeOffset? ScheduledAt,
    string? Location,
    string? MeetingUrl,
    string? Summary,
    DateTimeOffset CreatedAt);

public sealed record FederalOfficial(
    string BioguideId,
    string FullName,
    string Chamber,
    string State,
    string? District,
    string? Party,
    DateOnly? TermEndsOn,
    string? Website,
    string? Phone,
    string? Office);

// --- read models -----------------------------------------------------------

/// <summary>One entry in an organization's, contact's or engagement's timeline.</summary>
public sealed record TimelineEntry(
    Guid Id,
    InteractionKind Kind,
    DateTimeOffset OccurredAt,
    string Body,
    string? LoggedByName,
    string? ContactName,
    string? EngagementTitle);

public sealed record OrganizationSummary(
    Guid Id,
    OrganizationKind Kind,
    string Name,
    string? State,
    string? District,
    int ContactCount,
    int OpenEngagementCount,
    DateTimeOffset? LastInteractionAt);

public sealed record EngagementSummary(
    Guid Id,
    string Title,
    EngagementStatus Status,
    ActivityCategory? ActivityCategory,
    Guid? OrganizationId,
    string? OrganizationName,
    string? OwnerName,
    DateTimeOffset? ScheduledAt,
    int OpenTaskCount);

public sealed record CrmTaskSummary(
    Guid Id,
    string Title,
    CrmTaskStatus Status,
    DateOnly? DueOn,
    Guid? AssignedTo,
    string? AssignedToName,
    Guid? EngagementId,
    string? EngagementTitle,
    Guid? OrganizationId,
    string? OrganizationName)
{
    /// <summary>Open, dated, and past due on <paramref name="today"/>.</summary>
    public bool IsOverdueOn(DateOnly today) => Status == CrmTaskStatus.Open && DueOn is { } due && due < today;
}

/// <summary>
/// One message in an outreach thread. Team-visible by design: the point of
/// outreach mail living in ManyHands is that nothing hides in a personal inbox.
/// </summary>
public sealed record CrmEmail(
    Guid Id,
    EmailDirection Direction,
    Guid? OrganizationId,
    Guid? ContactId,
    Guid? EngagementId,
    string FromAddress,
    string ToAddress,
    string Subject,
    string BodyText,
    string? MessageId,
    string? InReplyTo,
    Guid ConversationId,
    EmailStatus Status,
    string? ProviderMessageId,
    string? Error,
    Guid? SentBy,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    bool NeedsTriage);

public sealed record EmailThreadSummary(
    Guid ConversationId,
    string Subject,
    string FromAddress,
    string ToAddress,
    DateTimeOffset LastMessageAt,
    EmailDirection Direction,
    string? OrganizationName,
    string? ContactName,
    int MessageCount,
    bool NeedsTriage);

public sealed record CrmEmailTemplate(
    Guid Id,
    string Name,
    string Subject,
    string BodyText,
    DateTimeOffset CreatedAt);
