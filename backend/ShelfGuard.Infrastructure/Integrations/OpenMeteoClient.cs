using System.Net.Http.Json;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Integrations;

/// <summary>
/// Open-Meteo forecast client (v2-spec §6). Free, no API key, up to 16 days.
/// Isolated here per architecture rule: integrations never leak into business logic.
/// </summary>
public sealed class OpenMeteoClient : IOpenMeteoClient
{
    private readonly HttpClient _http;

    public OpenMeteoClient(HttpClient http) => _http = http;

    public async Task<List<DailyForecast>> GetForecastAsync(
        decimal latitude, decimal longitude, int days = 7, CancellationToken ct = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode" +
                  $"&forecast_days={days}&timezone=UTC";

        var resp = await _http.GetFromJsonAsync<OpenMeteoResponse>(url, ct)
            ?? throw new HttpRequestException("Open-Meteo returned an empty response.");

        var daily = resp.Daily
            ?? throw new HttpRequestException("Open-Meteo response has no daily block.");

        var result = new List<DailyForecast>();
        for (var i = 0; i < daily.Time.Count; i++)
        {
            result.Add(new DailyForecast(
                DateOnly.Parse(daily.Time[i]),
                daily.Temperature2mMin?.ElementAtOrDefault(i),
                daily.Temperature2mMax?.ElementAtOrDefault(i),
                daily.PrecipitationSum?.ElementAtOrDefault(i),
                daily.WeatherCode?.ElementAtOrDefault(i)));
        }

        return result;
    }

    // ── wire DTOs ──────────────────────────────────────────────────────────

    private sealed class OpenMeteoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("daily")]
        public OpenMeteoDaily? Daily { get; set; }
    }

    private sealed class OpenMeteoDaily
    {
        [System.Text.Json.Serialization.JsonPropertyName("time")]
        public List<string> Time { get; set; } = [];

        [System.Text.Json.Serialization.JsonPropertyName("temperature_2m_max")]
        public List<decimal?>? Temperature2mMax { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("temperature_2m_min")]
        public List<decimal?>? Temperature2mMin { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("precipitation_sum")]
        public List<decimal?>? PrecipitationSum { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("weathercode")]
        public List<int?>? WeatherCode { get; set; }
    }
}
