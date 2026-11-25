namespace Website.Portal.Act;

/// <summary>
/// Configuration for the action centre under <c>/act</c>. Everything that
/// reaches a third party is off unless an environment turns it on: a developer
/// running the site, and every CI run, contacts nobody (the same rule as the
/// federal officials sync).
/// </summary>
public sealed class ActOptions
{
    public const string SectionName = "Act";

    /// <summary>
    /// Look addresses up with the Census Bureau's geocoder. Off, the page asks
    /// the reader to pick their districts instead, and says where to find them.
    /// </summary>
    public bool LookupEnabled { get; set; }

    /// <summary>The geocoder's one-line-address geographies endpoint.</summary>
    public string GeocoderUrl { get; set; } = "https://geocoding.geo.census.gov/geocoder/geographies/onelineaddress";

    public string Benchmark { get; set; } = "Public_AR_Current";

    public string Vintage { get; set; } = "Current_Current";

    /// <summary>
    /// Show AI record items nobody has checked yet. True in development (set in
    /// appsettings.Development.json), false everywhere else, so nothing
    /// unchecked reaches a reader. Nothing on the page marks an item either
    /// way: checking is done before the directory goes live, not shown to
    /// readers.
    /// </summary>
    public bool ShowUnverified { get; set; }
}

/// <summary>
/// DeepInfra, which runs the model that helps a reader put their message in
/// their own words. No key, no calls: the drafts come from templates instead,
/// and the page never mentions AI help.
/// </summary>
public sealed class DeepInfraOptions
{
    public const string SectionName = "DeepInfra";

    /// <summary>Secret. An App Platform environment variable in production (<c>DeepInfra__ApiKey</c>), user-secrets locally.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Any chat model DeepInfra serves through its OpenAI-compatible API.</summary>
    public string Model { get; set; } = "meta-llama/Llama-3.3-70B-Instruct";

    public string BaseUrl { get; set; } = "https://api.deepinfra.com/v1/openai/";

    /// <summary>A draft that has not come back by then is a template instead. A reader is waiting.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public int MaxTokens { get; set; } = 600;

    /// <summary>
    /// Model calls per day across everyone, after which drafts are templates
    /// until the day turns over. The per-address limit stops one reader; this
    /// stops a crowd of addresses running up the bill.
    /// </summary>
    public int DailyLimit { get; set; } = 500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
