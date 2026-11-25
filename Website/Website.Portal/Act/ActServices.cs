using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Website.Content;
using Website.Portal.Volunteer;

namespace Website.Portal.Act;

/// <summary>
/// Per-address limits on the action centre's forms, counted on the submit.
/// Looking up an address and drafting a message each call a third party for
/// us; drafting with a model costs money per call, so it is the tighter one.
/// Registered whether or not ManyHands is on: the action centre does not need it.
/// </summary>
public sealed class ActThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _lookup = Limiter(permits: 20, TimeSpan.FromMinutes(10));
    private readonly PartitionedRateLimiter<string> _draft = Limiter(permits: 10, TimeSpan.FromMinutes(10));
    private readonly PartitionedRateLimiter<string> _log = Limiter(permits: 30, TimeSpan.FromMinutes(10));

    public bool AllowLookup(HttpContext http) => Take(_lookup, http);

    public bool AllowDraft(HttpContext http) => Take(_draft, http);

    public bool AllowLog(HttpContext http) => Take(_log, http);

    private static bool Take(PartitionedRateLimiter<string> limiter, HttpContext http)
    {
        using var lease = limiter.AttemptAcquire(FormThrottle.ClientAddress(http));
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Limiter(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(address =>
            RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0
            }));

    public void Dispose()
    {
        _lookup.Dispose();
        _draft.Dispose();
        _log.Dispose();
    }
}

public static class ActServiceCollectionExtensions
{
    private const string UserAgent = "PhillyPause/1.0 (+https://phillypause.org)";

    /// <summary>
    /// The directory, the lookup and the drafter. The lookup and the model are
    /// each off until configured, and off means a stand-in that calls nobody.
    /// </summary>
    public static IServiceCollection AddAct(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddSingleton(new OfficialsStore(contentRoot));
        services.AddSingleton<ActThrottle>();

        services.AddOptions<ActOptions>().Bind(configuration.GetSection(ActOptions.SectionName))
            .Validate(o => !o.LookupEnabled || Uri.TryCreate(o.GeocoderUrl, UriKind.Absolute, out _),
                "Act:GeocoderUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddOptions<DeepInfraOptions>().Bind(configuration.GetSection(DeepInfraOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _) && o.BaseUrl.EndsWith('/'),
                "DeepInfra:BaseUrl must be an absolute URL ending in a slash.")
            .Validate(o => o.Timeout > TimeSpan.Zero && o.Timeout <= TimeSpan.FromMinutes(1),
                "DeepInfra:Timeout must be between zero and a minute.")
            .Validate(o => o.MaxTokens is > 0 and <= 2000, "DeepInfra:MaxTokens must be between 1 and 2000.")
            .Validate(o => o.DailyLimit is > 0 and <= 100_000, "DeepInfra:DailyLimit must be between 1 and 100,000.")
            .ValidateOnStart();

        var act = configuration.GetSection(ActOptions.SectionName).Get<ActOptions>() ?? new();
        if (act.LookupEnabled)
        {
            services.AddHttpClient<CensusGeocoderLookup>(http =>
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            });
            services.AddTransient<IDistrictLookup>(sp => sp.GetRequiredService<CensusGeocoderLookup>());
        }
        else
        {
            services.AddSingleton<IDistrictLookup, NoDistrictLookup>();
        }

        var model = configuration.GetSection(DeepInfraOptions.SectionName).Get<DeepInfraOptions>() ?? new();
        if (model.IsConfigured)
        {
            services.AddSingleton<ModelBudget>();
            services.AddHttpClient<DeepInfraDrafter>(http =>
            {
                // The drafter's own timeout is the one that matters; this is the backstop.
                http.Timeout = TimeSpan.FromMinutes(1);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            });
            services.AddTransient<IMessageDrafter>(sp => sp.GetRequiredService<DeepInfraDrafter>());
        }
        else
        {
            services.AddSingleton<IMessageDrafter, TemplateDrafter>();
        }

        return services;
    }
}
