using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Integrations;

/// <summary>
/// OpenStreetMap Nominatim geocoder (address → lat/lon). Free, no API key.
/// Isolated here per architecture rule: integrations never leak into business logic.
/// Nominatim rejects requests without a User-Agent, so one is sent on every call.
/// </summary>
public sealed class NominatimGeocodingClient : IGeocodingClient
{
    private const string UserAgent = "ShelfGuard/1.0 (+https://agrusystems.pp.ua)";

    private readonly HttpClient _http;

    public NominatimGeocodingClient(HttpClient http) => _http = http;

    public async Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        // countrycodes=ua constrains matches to Ukraine — every tenant is Ukrainian, and it
        // stops a loosely-phrased Cyrillic address from occasionally matching a same-named
        // street abroad (observed once resolving "Київ, вулиця Хрещатик 22" to Brittany, FR).
        var url = "https://nominatim.openstreetmap.org/search" +
                  $"?q={Uri.EscapeDataString(query)}&format=jsonv2&limit=1&accept-language=uk&countrycodes=ua";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", UserAgent);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var places = await response.Content.ReadFromJsonAsync<List<NominatimPlace>>(cancellationToken: ct);
        var place = places?.FirstOrDefault();
        if (place is null
            || !decimal.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !decimal.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeocodeResult(lat, lon, string.IsNullOrWhiteSpace(place.DisplayName) ? query : place.DisplayName);
    }

    // ── wire DTO ───────────────────────────────────────────────────────────

    private sealed class NominatimPlace
    {
        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}
