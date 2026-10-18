using System.Globalization;
using System.Text;
using ManyHands.Core;
using ManyHands.Data;

namespace Website.Portal.Volunteer;

/// <summary>The team's one download: who came to what.</summary>
public static class TeamEndpoints
{
    public const string ParticipationPath = "/team/participation.csv";

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        // CSV on purpose: every organizer can open one, and it is where any
        // report ends up anyway.
        app.MapGet(ParticipationPath, async (ReportingRepository reporting, CancellationToken ct) =>
        {
            var rows = await reporting.GetParticipationAsync(ct);
            var csv = Csv.Build(
                ["Volunteer", "Email", "Activity", "Types", "Starts (Eastern)", "Status", "Checked in"],
                rows.Select(r => new[]
                {
                    r.DisplayName, r.Email ?? string.Empty, r.Title, r.Types,
                    r.StartsAt is { } s ? LocalTime.ToLocal(s).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty,
                    r.Status.ToString(),
                    r.CheckedInAt is null ? "no" : "yes"
                }));

            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv", "participation.csv");
        })
        .RequireAuthorization(TeamPolicies.Organizer);

        return app;
    }
}

public static class Csv
{
    public static string Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var row in rows) builder.AppendLine(string.Join(',', row.Select(Cell)));
        return builder.ToString();
    }

    /// <summary>
    /// Names and emails here were typed into public forms. A cell starting
    /// with =, +, - or @ is a formula to a spreadsheet, so it is defused with a
    /// leading apostrophe before an organizer opens the file.
    /// </summary>
    public static string Cell(string value)
    {
        if (value.Length == 0) return value;

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;

        var quote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return quote ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
