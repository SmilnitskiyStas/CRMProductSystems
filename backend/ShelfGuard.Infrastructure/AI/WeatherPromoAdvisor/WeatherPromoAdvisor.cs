using System.Globalization;
using System.Text.Json;
using ShelfGuard.Application.Features.WeatherPromo;
using ShelfGuard.Application.Features.WeatherPromo.Dtos;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI.WeatherPromoAdvisor;

/// <summary>
/// Weekly weather-promo advisor (TASK-711). New folder — deliberately isolated from the
/// concurrently-evolving managed-AI advisors; uses only the stable Phase-4a primitives
/// (<see cref="IAiClientFactory"/>, <see cref="AiSlot"/>, <see cref="IAiPromptResolver"/>).
///
/// Prompt template lives here per ADR-015; the model call is provider-agnostic (Claude or an
/// OpenAI-compatible endpoint, per the tenant's Analyst-slot config). Structured output. The
/// per-tenant isolation guardrail is applied by <see cref="IAiPromptResolver"/>. Context is
/// assembled by <c>WeatherPromoService</c> and passed in as <see cref="WeatherPromoContext"/> —
/// this class never touches the database.
/// </summary>
public sealed class WeatherPromoAdvisor : IWeatherPromoAdvisor
{
    private const AiSlot Slot = AiSlot.Analyst;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public WeatherPromoAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<WeatherPromoAdviceResult> RecommendAsync(WeatherPromoContext ctx, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), Slot, ct);
        var result = await client.CompleteAsync(
            new AiChatRequest(system, BuildUserPrompt(ctx), MaxTokens: 4096, JsonSchema: ResponseSchemaJson), ct);

        return new WeatherPromoAdviceResult(ParseSuggestions(result.Text), result.Model, result.TokensUsed);
    }

    // ── Prompts ─────────────────────────────────────────────────────────────

    private static string BuildSystemPrompt() =>
        "Ти — AI аналітик роздрібного магазину в Україні. На основі прогнозу погоди, історії " +
        "продажів і погодних коефіцієнтів запропонуй 1–4 тижневі акції: які товари вигідно " +
        "поставити на знижку перед сприятливою погодою, щоб продати активніше. Обґрунтування — " +
        "коротко, українською. Не пропонуй товари, яких немає в наданому списку. Відповідай " +
        "ТІЛЬКИ JSON за схемою.";

    internal static string BuildUserPrompt(WeatherPromoContext ctx)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        return
            $"СЬОГОДНІ: {ctx.Today:yyyy-MM-dd}\n\n" +
            $"ПРОГНОЗ ПОГОДИ (наступні ~14 днів, по локаціях):\n{JsonSerializer.Serialize(ctx.Forecast, opts)}\n\n" +
            $"ТОП-ПРОДАЖІ (останні 45 днів, за кількістю):\n{JsonSerializer.Serialize(ctx.TopSellers, opts)}\n\n" +
            $"ПОГОДНІ КОЕФІЦІЄНТИ ПОПИТУ (проксі «що погодозалежне»):\n{JsonSerializer.Serialize(ctx.WeatherCoefficients, opts)}\n\n" +
            $"ВЖЕ АКТИВНІ АКЦІЇ (не дублюй):\n{JsonSerializer.Serialize(ctx.ActivePromos, opts)}\n\n" +
            $"ТОВАРИ ВЖЕ ЗІ ЗНИЖКОЮ (не дублюй):\n{JsonSerializer.Serialize(ctx.ActivelyDiscountedProducts, opts)}\n\n" +
            "Використовуй ТІЛЬКИ item_id зі списку топ-продажів. Дати вікна — у форматі YYYY-MM-DD. " +
            "recommended_discount_pct — ціле або дробове число відсотків (1–90).";
    }

    private const string ResponseSchemaJson = """
        {
          "type": "object",
          "properties": {
            "suggestions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title":       { "type": "string" },
                  "weather_window": {
                    "type": "object",
                    "properties": {
                      "starts_at": { "type": "string" },
                      "ends_at":   { "type": "string" },
                      "summary":   { "type": "string" }
                    },
                    "required": ["starts_at", "ends_at", "summary"],
                    "additionalProperties": false
                  },
                  "rationale":                { "type": "string" },
                  "recommended_discount_pct": { "type": "number" },
                  "confidence":               { "type": "string", "enum": ["low", "medium", "high"] },
                  "products": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "item_id": { "type": "string" },
                        "name":    { "type": "string" },
                        "reason":  { "type": "string" }
                      },
                      "required": ["item_id", "name", "reason"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["title", "weather_window", "rationale", "recommended_discount_pct", "confidence", "products"],
                "additionalProperties": false
              }
            }
          },
          "required": ["suggestions"],
          "additionalProperties": false
        }
        """;

    /// <summary>Internal + static so unit tests can exercise parsing without an API call.</summary>
    internal static List<WeatherPromoSuggestion> ParseSuggestions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<WeatherPromoSuggestion>();

        if (!doc.RootElement.TryGetProperty("suggestions", out var suggestionsEl) ||
            suggestionsEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var s in suggestionsEl.EnumerateArray())
        {
            var window = s.GetProperty("weather_window");
            if (!TryParseDate(window, "starts_at", out var startsAt) ||
                !TryParseDate(window, "ends_at", out var endsAt))
                continue; // no usable window — the suggestion can't be applied

            var products = new List<WeatherPromoProduct>();
            if (s.TryGetProperty("products", out var productsEl) && productsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in productsEl.EnumerateArray())
                {
                    if (!p.TryGetProperty("item_id", out var idEl) ||
                        !Guid.TryParse(idEl.GetString(), out var itemId))
                        continue; // AI hallucinated an id — skip the line

                    products.Add(new WeatherPromoProduct(
                        itemId,
                        GetString(p, "name"),
                        CurrentPrice: null, // filled by the service from its top-seller lookup
                        GetString(p, "reason")));
                }
            }

            result.Add(new WeatherPromoSuggestion(
                GetString(s, "title"),
                startsAt,
                endsAt,
                GetString(window, "summary"),
                GetString(s, "rationale"),
                GetDecimal(s, "recommended_discount_pct"),
                GetString(s, "confidence", "medium"),
                products));
        }

        return result;
    }

    private static bool TryParseDate(JsonElement parent, string name, out DateOnly value)
    {
        value = default;
        return parent.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string GetString(JsonElement el, string name, string fallback = "") =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static decimal GetDecimal(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? Math.Round(v.GetDecimal(), 2)
            : 0m;
}
