using System.Text.Json;
using Microsoft.Extensions.Options;
using Website.Content;

namespace Website.Portal.Act;

/// <summary>Turns an address into the districts it sits in.</summary>
public interface IDistrictLookup
{
    /// <summary>False when lookups are off: the page offers the pick-your-districts form instead.</summary>
    bool IsAvailable { get; }

    /// <summary>The districts, or null when the address could not be found or the service failed.</summary>
    Task<DistrictMatch?> LookupAsync(string address, CancellationToken ct = default);
}

/// <summary>Lookups switched off (the default): nothing calls out.</summary>
public sealed class NoDistrictLookup : IDistrictLookup
{
    public bool IsAvailable => false;

    public Task<DistrictMatch?> LookupAsync(string address, CancellationToken ct = default) =>
        Task.FromResult<DistrictMatch?>(null);
}

/// <summary>
/// The US Census Bureau's public geocoder: free, keyless, and run by the
/// people who draw the district lines. The address goes to the Bureau and is
/// not kept by us; only the district numbers come back to the reader's
/// browser, in the address bar.
/// </summary>
public sealed class CensusGeocoderLookup(
    HttpClient http,
    IOptions<ActOptions> options,
    ILogger<CensusGeocoderLookup> logger) : IDistrictLookup
{
    public bool IsAvailable => true;

    public async Task<DistrictMatch?> LookupAsync(string address, CancellationToken ct = default)
    {
        var o = options.Value;
        var url = $"{o.GeocoderUrl}?address={Uri.EscapeDataString(address)}&benchmark={Uri.EscapeDataString(o.Benchmark)}"
                  + $"&vintage={Uri.EscapeDataString(o.Vintage)}&format=json";
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Census geocoder answered {Status}", (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(json.RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Never the address in the log: it is the reader's, not ours.
            logger.LogWarning(e, "Census geocoder lookup failed");
            return null;
        }
    }

    /// <summary>
    /// Reads the first match. Layer names carry the Congress number and the
    /// year of the legislative map ("119th Congressional Districts", "2024
    /// State Legislative Districts - Upper"), and those change, so layers are
    /// found by what they are rather than by their exact name.
    /// </summary>
    public static DistrictMatch? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("addressMatches", out var matches)
            || matches.ValueKind != JsonValueKind.Array
            || matches.GetArrayLength() == 0
            || !matches[0].TryGetProperty("geographies", out var geographies))
        {
            return null;
        }

        string? congressional = null, upper = null, lower = null, county = null, state = null;

        foreach (var layer in geographies.EnumerateObject())
        {
            if (layer.Value.ValueKind != JsonValueKind.Array || layer.Value.GetArrayLength() == 0) continue;
            var first = layer.Value[0];
            var name = layer.Name;

            if (name.Contains("Congressional District", StringComparison.OrdinalIgnoreCase))
                congressional ??= FieldStartingWith(first, "CD") ?? Text(first, "BASENAME");
            else if (name.Contains("Legislative Districts - Upper", StringComparison.OrdinalIgnoreCase))
                upper ??= Text(first, "SLDU") ?? Text(first, "BASENAME");
            else if (name.Contains("Legislative Districts - Lower", StringComparison.OrdinalIgnoreCase))
                lower ??= Text(first, "SLDL") ?? Text(first, "BASENAME");
            else if (name.Equals("Counties", StringComparison.OrdinalIgnoreCase))
                county ??= Text(first, "BASENAME") ?? Text(first, "NAME");
            else if (name.Equals("States", StringComparison.OrdinalIgnoreCase))
                state ??= Text(first, "STUSAB");
        }

        // Fall back on the address components when the States layer is absent.
        if (state is null && matches[0].TryGetProperty("addressComponents", out var components))
            state = Text(components, "state");

        var inPennsylvania = string.Equals(state, "PA", StringComparison.OrdinalIgnoreCase);
        return new DistrictMatch(
            inPennsylvania ? DistrictMatch.Normalise(congressional) : null,
            inPennsylvania ? DistrictMatch.Normalise(upper) : null,
            inPennsylvania ? DistrictMatch.Normalise(lower) : null,
            inPennsylvania ? DistrictMatch.NormaliseCounty(county) : null,
            inPennsylvania);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>"CD119" today, "CD120" one day: the field named for the Congress.</summary>
    private static string? FieldStartingWith(JsonElement element, string prefix) =>
        element.EnumerateObject()
            .Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal) && p.Name.Length > prefix.Length
                        && char.IsDigit(p.Name[prefix.Length]) && p.Value.ValueKind == JsonValueKind.String)
            .Select(p => p.Value.GetString())
            .FirstOrDefault();
}
