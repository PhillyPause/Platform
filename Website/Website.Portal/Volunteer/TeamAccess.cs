using ManyHands.Core;
using ManyHands.Data;
using Microsoft.AspNetCore.Authorization;

namespace Website.Portal.Volunteer;

/// <summary>The two doors on the team pages: organizers, and the admins who decide who is one.</summary>
public static class TeamPolicies
{
    public const string Organizer = "team-organizer";
    public const string Admin = "team-admin";

    public static void Add(AuthorizationOptions options)
    {
        options.AddPolicy(Organizer, p => p.AddRequirements(new TeamRoleRequirement(VolunteerRole.Organizer)));
        options.AddPolicy(Admin, p => p.AddRequirements(new TeamRoleRequirement(VolunteerRole.Admin)));
    }
}

public sealed record TeamRoleRequirement(VolunteerRole Role) : IAuthorizationRequirement;

/// <summary>
/// Reads the role from the database on every request, never from the cookie,
/// so taking someone's role away takes effect on their next click.
/// </summary>
public sealed class TeamRoleHandler(VolunteerRepository volunteers) : AuthorizationHandler<TeamRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TeamRoleRequirement requirement)
    {
        if (context.User.VolunteerId() is Guid id
            && await volunteers.GetByIdAsync(id) is { } volunteer
            && volunteer.IsAtLeast(requirement.Role))
        {
            context.Succeed(requirement);
        }
    }
}
