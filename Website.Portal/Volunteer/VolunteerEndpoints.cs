using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Calendar;

namespace Website.Portal.Volunteer;

/// <summary>
/// The few things that are not pages: the calendar feeds, a volunteer's data
/// export, and the one-click unsubscribe a mail client posts to by itself.
/// </summary>
public static class VolunteerEndpoints
{
    private const string CalendarContentType = "text/calendar; charset=utf-8";

    /// <summary>
    /// Off means off: with ManyHands disabled, everything under its paths is a
    /// plain 404, so no page reaches for a service that was never registered.
    /// </summary>
    public static IApplicationBuilder UseManyHandsGate(this IApplicationBuilder app, bool enabled) =>
        enabled
            ? app
            : app.Use((http, next) =>
            {
                if (!IsManyHandsPath(http.Request.Path)) return next(http);

                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });

    internal static bool IsManyHandsPath(PathString path) =>
        path.StartsWithSegments(ManyHandsPaths.Activities) || path.StartsWithSegments(ManyHandsPaths.Team);

    public static IEndpointRouteBuilder MapVolunteerEndpoints(this IEndpointRouteBuilder app)
    {
        // RFC 8058: the mail client posts "List-Unsubscribe=One-Click" here with
        // no page, no cookie and no antiforgery token. The opaque key is the
        // authority, and the answer is the same whether it matched anyone, so
        // the URL cannot be used to test whether an address is real.
        app.MapPost("/volunteer/unsubscribe/{commsKey:guid}/one-click", async (
            Guid commsKey, VolunteerRepository volunteers, CancellationToken ct) =>
        {
            await volunteers.UnsubscribeByCommsKeyAsync(commsKey, ct);
            return Results.Text("Unsubscribed.");
        })
        .DisableAntiforgery();

        app.MapGet(ManyHandsPaths.Calendar, async (
            HttpContext http, CalendarRepository calendar, CancellationToken ct) =>
        {
            var ics = IcsWriter.Write($"{Organisation.Name} activities", await calendar.GetPublicFeedAsync(ct));

            // Calendar apps poll on their own schedule and cache hard. Fifteen
            // minutes keeps a newly published activity from taking hours to
            // appear, without inviting a poll every minute.
            http.Response.Headers.CacheControl = "public, max-age=900";
            return Feed(http, ics, "phillypause-activities.ics");
        });

        app.MapGet("/volunteer/calendar/{calendarKey:guid}.ics", async (
            HttpContext http, Guid calendarKey, CalendarRepository calendar, CancellationToken ct) =>
        {
            var entries = await calendar.GetVolunteerFeedAsync(calendarKey, ct);
            if (entries is null) return Results.NotFound();

            // Private, and never in a shared cache: the URL is the credential.
            http.Response.Headers.CacheControl = "private, max-age=300";
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            return Feed(http, IcsWriter.Write($"My {Organisation.Name} activities", entries), "my-activities.ics");
        });

        app.MapGet(ManyHandsPaths.Export, async (
            HttpContext http, VolunteerDataRepository data, CancellationToken ct) =>
        {
            if (http.User.VolunteerId() is not Guid volunteerId) return Results.Unauthorized();

            var export = await data.ExportAsync(volunteerId, ct);
            if (export is null) return Results.NotFound();

            // A file to keep, not a page to read.
            http.Response.Headers.ContentDisposition = "attachment; filename=\"my-phillypause-data.json\"";
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(export);
        })
        .RequireAuthorization();

        return app;
    }

    /// <summary>Inline rather than an attachment: subscribing is the point.</summary>
    private static IResult Feed(HttpContext http, string ics, string fileName)
    {
        http.Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return Results.Text(ics, CalendarContentType);
    }
}
