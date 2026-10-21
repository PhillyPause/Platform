using System.Security.Claims;
using ManyHands.Core;
using ManyHands.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Website.Portal.Volunteer;

/// <summary>
/// A volunteer's sign-in: a cookie carrying who they are, issued when they
/// redeem an emailed link. What they may do is read from the database each
/// time, never from the cookie, so a changed or removed role takes effect at
/// once. The cookie's name and role are for display only — the account bar
/// on every page reads them, so a page never waits on the database to say who
/// is signed in — and are re-checked every few minutes.
/// </summary>
public static class VolunteerSession
{
    public const string CookieName = "phillypause.session";

    /// <summary>How stale the name and role shown in the account bar may get.</summary>
    internal static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(5);

    /// <summary>The signed-in volunteer's id, or null for a reader who is not signed in.</summary>
    public static Guid? VolunteerId(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    /// <summary>Who to say is signed in. Display only; never a basis for access.</summary>
    public static string? DisplayName(this ClaimsPrincipal user) =>
        user.VolunteerId() is null ? null : user.FindFirstValue(ClaimTypes.Name);

    /// <summary>Whether to show the way into the team pages. Display only; the pages check the database.</summary>
    public static bool ShowsTeam(this ClaimsPrincipal user) =>
        user.VolunteerId() is not null
        && Enum.TryParse<VolunteerRole>(user.FindFirstValue(ClaimTypes.Role), out var role)
        && role >= VolunteerRole.Organizer;

    public static Task SignInAsync(HttpContext http, ManyHands.Core.Volunteer volunteer) =>
        http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            Principal(volunteer),
            new AuthenticationProperties { IsPersistent = true });

    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    private static ClaimsPrincipal Principal(ManyHands.Core.Volunteer volunteer) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, volunteer.Id.ToString()),
        new Claim(ClaimTypes.Name, volunteer.DisplayName),
        new Claim(ClaimTypes.Role, volunteer.Role.ToString())
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>
    /// The cookie: HttpOnly, Lax because sign-in links arrive as top-level
    /// navigations, and 30 days sliding, so a regular volunteer stays signed in.
    /// </summary>
    public static IServiceCollection AddVolunteerSession(this IServiceCollection services, bool isDevelopment)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = isDevelopment
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.LoginPath = ManyHandsPaths.SignIn;

                // A member who wanders onto a team page is told it does not
                // exist, not that it is forbidden: there is nothing to see.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                };

                options.Events.OnValidatePrincipal = RecheckAsync;
            });

        services.AddAuthorization(TeamPolicies.Add);
        return services;
    }

    /// <summary>
    /// Every <see cref="RecheckEvery"/>, the name and role in the cookie are
    /// brought in line with the database, and a deleted volunteer is signed
    /// out. A database that cannot be reached keeps the cookie as it is: the
    /// account bar may be a few minutes stale, never a reason for a page to fail.
    /// </summary>
    private static async Task RecheckAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.VolunteerId() is not Guid id) return;
        if (context.Properties.IssuedUtc is DateTimeOffset issued && DateTimeOffset.UtcNow - issued < RecheckEvery) return;

        // Off, nothing can sign anyone in, and a leftover cookie names nobody.
        if (context.HttpContext.RequestServices.GetService<VolunteerRepository>() is not { } volunteers)
        {
            context.RejectPrincipal();
            return;
        }

        ManyHands.Core.Volunteer? volunteer;
        try
        {
            volunteer = await volunteers.GetByIdAsync(id, context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(VolunteerSession))
                .LogWarning(ex, "Could not re-check the session for volunteer {VolunteerId}; keeping it", id);
            return;
        }

        if (volunteer is null || volunteer.IsDeleted)
        {
            context.RejectPrincipal();
            await SignOutAsync(context.HttpContext);
            return;
        }

        // Renewed either way, which resets IssuedUtc and so the next re-check.
        context.ReplacePrincipal(Principal(volunteer));
        context.ShouldRenew = true;
    }
}
