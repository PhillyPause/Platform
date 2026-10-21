using ManyHands.Core;
using ManyHands.Data;
using Microsoft.Extensions.Options;

namespace Website.Portal.Volunteer;

/// <summary>
/// How the first admin exists: an address listed in <c>ManyHands:Admins</c>
/// becomes an admin when it signs in. After that, admins make each other on
/// the members page; taking an address off the list demotes nobody.
/// </summary>
public sealed class AdminBootstrap(IOptions<ManyHandsOptions> options, VolunteerRepository volunteers)
{
    public async Task ApplyAsync(Guid volunteerId, CancellationToken ct = default)
    {
        if (options.Value.Admins.Length == 0) return;

        if (await volunteers.GetByIdAsync(volunteerId, ct) is { Email: { } email, Role: < VolunteerRole.Admin }
            && options.Value.Admins.Contains(email.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            await volunteers.PromoteAsync(volunteerId, VolunteerRole.Admin, ct);
        }
    }
}
