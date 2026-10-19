using System.Text.RegularExpressions;
using ManyHands.Data.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ManyHands.Tests;

/// <summary>
/// The real portal, in memory, with ManyHands on and pointed at the test
/// database. The background workers are left out: they would dispatch rows
/// other tests queued and send mail those tests count.
/// </summary>
public sealed class PortalFactory(string connectionString, params (string Key, string Value)[] settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ManyHands:Enabled", "true");
        builder.UseSetting("ConnectionStrings:ManyHands", connectionString);

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            foreach (var worker in services
                         .Where(d => d.ServiceType == typeof(IHostedService)
                                     && (d.ImplementationType == typeof(OutboxWorker)
                                         || d.ImplementationType == typeof(ReminderWorker)
                                         || d.ImplementationType == typeof(ManyHands.Data.Discord.DiscordRoleSyncWorker)))
                         .ToList())
            {
                services.Remove(worker);
            }
        });
    }

    /// <summary>A client that keeps cookies and shows redirects rather than following them.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}

/// <summary>What a browser does with a static-SSR form: read the token, post the fields.</summary>
internal static partial class Forms
{
    public static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, string path, string handler, params (string Name, string Value)[] fields)
    {
        var page = await client.GetStringAsync(path);
        var token = Token().Match(page).Groups[1].Value;

        var body = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("_handler", handler)
        };
        body.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));

        return await client.PostAsync(path, new FormUrlEncodedContent(body));
    }

    /// <summary>Signs a browser in as this volunteer, the way a person does: a link, then the button.</summary>
    public static async Task SignInAsync(HttpClient client, Npgsql.NpgsqlDataSource db, Guid volunteerId)
    {
        var token = await new ManyHands.Data.AuthTokenRepository(db).IssueAsync(volunteerId);
        await SubmitAsync(client, ManyHands.Core.ManyHandsPaths.RedeemSignIn(token), "redeem");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Token();
}
