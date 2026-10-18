using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Website.Portal.Volunteer;

/// <summary>
/// A volunteer's sign-in: a cookie carrying who they are, issued when they
/// redeem an emailed link. What they may do is read from the database each
/// time, never from the cookie, so a changed or removed role takes effect at
/// once.
/// </summary>
public static class VolunteerSession
{
    public const string CookieName = "phillypause.session";

    /// <summary>The signed-in volunteer's id, or null for a reader who is not signed in.</summary>
    public static Guid? VolunteerId(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public static Task SignInAsync(HttpContext http, Guid volunteerId, string displayName)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, volunteerId.ToString()),
            new Claim(ClaimTypes.Name, displayName)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

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
                options.LoginPath = ManyHands.Core.ManyHandsPaths.SignIn;

                // A member who wanders onto a team page is told it does not
                // exist, not that it is forbidden: there is nothing to see.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(TeamPolicies.Add);
        return services;
    }
}
