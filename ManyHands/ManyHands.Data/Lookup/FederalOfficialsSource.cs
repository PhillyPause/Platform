using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManyHands.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManyHands.Data.Lookup;

/// <summary>
/// Where the federal directory comes from. An interface because the sync
/// worker has to be testable without the public internet, and because the
/// upstream dataset is somebody else's project that could move.
/// </summary>
public interface IFederalOfficialsSource
{
    Task<IReadOnlyList<FederalOfficial>> FetchAsync(CancellationToken ct = default);
}

/// <summary>Configuration for the federal directory sync.</summary>
public sealed class FederalOfficialsOptions
{
    public const string SectionName = "FederalOfficials";

    public const string DefaultSourceUrl =
        "https://unitedstates.github.io/congress-legislators/legislators-current.json";

    /// <summary>
    /// Off unless an environment opts in, and that default is load-bearing: a
    /// developer running the app, and every CI run, must reach no third-party
    /// host at all, the same principle as needing no secrets to run it. Only
    /// production sets this true.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The public <c>unitedstates/congress-legislators</c> dataset. The JSON
    /// rendering, deliberately: the canonical file is YAML, and a whole YAML
    /// parser is a poor trade for one weekly GET.
    /// </summary>
    public string SourceUrl { get; set; } = DefaultSourceUrl;

    /// <summary>
    /// Congress changes on the timescale of elections, not minutes. Weekly is
    /// already generous, and it doubles as the staleness threshold — a sync is
    /// skipped outright when the last successful one is newer than this.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Sync on boot rather than waiting out a whole interval. Safe because the
    /// staleness check runs first: a worker that restarts ten times in an hour
    /// still makes one request.
    /// </summary>
    public bool RunOnStartup { get; set; } = true;
}

/// <summary>
/// Reads the current Congress from the public
/// <c>unitedstates/congress-legislators</c> dataset over a typed HttpClient
/// (the same shape as <c>ResendEmailSender</c>).
///
/// The dataset is a third party's volunteer-maintained file, so every field is
/// treated as optional and any record that cannot become a row the database
/// would accept is skipped and counted rather than thrown. One malformed
/// member must not cost us the other 537.
/// </summary>
public sealed class CongressLegislatorsSource(
    HttpClient http,
    IOptions<FederalOfficialsOptions> options,
    ILogger<CongressLegislatorsSource> logger) : IFederalOfficialsSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Districts arrive as JSON numbers today; tolerate them arriving
        // quoted tomorrow rather than failing the whole sync over it.
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<IReadOnlyList<FederalOfficial>> FetchAsync(CancellationToken ct = default)
    {
        var url = options.Value.SourceUrl;

        // ResponseHeadersRead keeps a multi-megabyte body out of the resilience
        // handler's per-attempt timeout, and out of a buffer.
        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await ParseAsync(stream, ct).ConfigureAwait(false);

        if (result.Skipped > 0)
        {
            logger.LogWarning(
                "Skipped {Skipped} of {Total} records from {Url}: no bioguide id, no terms, " +
                "or a current term this schema cannot store",
                result.Skipped, result.Skipped + result.Officials.Count, url);
        }

        logger.LogInformation(
            "Fetched {Count} federal officials from {Url}", result.Officials.Count, url);

        return result.Officials;
    }

    /// <summary>What the dataset yielded, and how much of it was unusable.</summary>
    internal sealed record FetchResult(IReadOnlyList<FederalOfficial> Officials, int Skipped);

    /// <summary>
    /// Split out from the HTTP call so the mapping — which is where the real
    /// risk lives — is covered by unit tests that touch no network.
    /// </summary>
    internal static async Task<FetchResult> ParseAsync(Stream json, CancellationToken ct = default)
    {
        var records = await JsonSerializer
            .DeserializeAsync<List<LegislatorRecord>>(json, SerializerOptions, ct)
            .ConfigureAwait(false) ?? [];

        var officials = new List<FederalOfficial>(records.Count);
        var skipped = 0;

        foreach (var record in records)
        {
            if (ToOfficial(record) is { } official)
                officials.Add(official);
            else
                skipped++;
        }

        return new FetchResult(officials, skipped);
    }

    /// <summary>
    /// Null means "cannot be stored" — the caller counts it and moves on. Every
    /// rejection here mirrors a NOT NULL or CHECK constraint on
    /// <c>federal_officials</c>, so the sync never hands the database a
    /// row it is going to refuse.
    /// </summary>
    private static FederalOfficial? ToOfficial(LegislatorRecord record)
    {
        var bioguideId = Clean(record.Id?.Bioguide);
        if (bioguideId is null) return null;

        // Terms are chronological, so a member's current one is the last.
        if (record.Terms is not { Count: > 0 } terms) return null;
        var term = terms[^1];

        var chamber = term.Type?.Trim().ToLowerInvariant() switch
        {
            "rep" => "house",
            "sen" => "senate",
            _ => null
        };
        if (chamber is null) return null;

        // CHECK (state ~ '^[A-Z]{2}$') — territories and DC already fit it.
        var state = Clean(term.State)?.ToUpperInvariant();
        if (state is not { Length: 2 } || !state.All(char.IsAsciiLetterUpper)) return null;

        var fullName = FullName(record.Name);
        if (fullName is null) return null;

        return new FederalOfficial(
            bioguideId,
            fullName,
            chamber,
            state,
            // Senators have no district. The dataset numbers House districts and
            // uses 0 for at-large, which is exactly what "00" means in the
            // conventional two-digit district code.
            term.District?.ToString("00", CultureInfo.InvariantCulture),
            Clean(term.Party),
            ParseDate(term.End),
            Clean(term.Url),
            Clean(term.Phone),
            Clean(term.Office));
    }

    /// <summary>
    /// <c>official_full</c> is the name the member goes by and is present for
    /// essentially everyone; first + last is the fallback for the rest.
    /// </summary>
    private static string? FullName(LegislatorName? name)
    {
        if (name is null) return null;
        if (Clean(name.OfficialFull) is { } official) return official;

        var fallback = $"{Clean(name.First)} {Clean(name.Last)}".Trim();
        return fallback.Length == 0 ? null : fallback;
    }

    /// <summary>ISO dates only — a surprise format is a null term end, not a crash.</summary>
    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // --- wire shapes -------------------------------------------------------
    // Every property is nullable on purpose: this is somebody else's file, and
    // "the field is missing" has to be a mapping decision, not an exception.

    private sealed record LegislatorRecord(
        [property: JsonPropertyName("id")] LegislatorId? Id,
        [property: JsonPropertyName("name")] LegislatorName? Name,
        [property: JsonPropertyName("terms")] IReadOnlyList<LegislatorTerm>? Terms);

    private sealed record LegislatorId(
        [property: JsonPropertyName("bioguide")] string? Bioguide);

    private sealed record LegislatorName(
        [property: JsonPropertyName("first")] string? First,
        [property: JsonPropertyName("last")] string? Last,
        [property: JsonPropertyName("official_full")] string? OfficialFull);

    private sealed record LegislatorTerm(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("district")] int? District,
        [property: JsonPropertyName("party")] string? Party,
        [property: JsonPropertyName("end")] string? End,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("office")] string? Office);
}
