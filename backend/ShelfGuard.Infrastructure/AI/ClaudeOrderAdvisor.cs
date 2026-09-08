using System.Text.Json;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// AI order advisor (v2-spec §7). Prompt template lives here per the architecture rule; the
/// model call is provider-agnostic since managed-AI Phase 2 — through <see cref="IAiClientFactory"/>
/// / <see cref="IAiChatClient"/> (Claude or an OpenAI-compatible endpoint, per the tenant's
/// provider config). Uses structured outputs so the response is guaranteed-valid JSON. The
/// per-tenant isolation guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
/// </summary>
public sealed class ClaudeOrderAdvisor : IAiOrderAdvisor
{
    private const AiSlot Slot = AiSlot.Analyst;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public ClaudeOrderAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<AiAdviceResult> AdviseAsync(AiOrderContext context, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), Slot, ct);
        var result = await client.CompleteAsync(
            new AiChatRequest(system, BuildUserPrompt(context), MaxTokens: 8192, JsonSchema: ResponseSchemaJson), ct);

        var items = ParseAdvice(result.Text);
        return new AiAdviceResult(items, result.Model, result.TokensUsed);
    }

    // ── prompt (v2-spec §7 template) ───────────────────────────────────────

    private static string BuildSystemPrompt() =>
        "Ти — AI асистент менеджера продуктового магазину в Україні. " +
        "Проаналізуй дані та скоригуй автоматично розраховане замовлення. " +
        "Для кожного товару де base_order_qty > 0: скоригуй кількість якщо є вагомі причини " +
        "(погода, події, акції); напиши коротке обґрунтування одним реченням українською; " +
        "вкажи confidence (high/medium/low) та фактори-множники. " +
        "Якщо причин коригувати немає — поверни base_order_qty без змін з фактором 1.0. " +
        "Кількості округляй до цілих. Відповідай ТІЛЬКИ JSON за заданою схемою.";

    private static string BuildUserPrompt(AiOrderContext c)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        return
            $"КОНТЕКСТ МАГАЗИНУ:\n" +
            $"  Назва: {c.StoreName}\n" +
            $"  Дата замовлення: {c.OrderDate:yyyy-MM-dd}\n" +
            $"  Наступна поставка: {c.NextDeliveryDate?.ToString("yyyy-MM-dd") ?? "невідомо"}\n\n" +
            $"ПОГОДА (прогноз):\n{JsonSerializer.Serialize(c.WeatherForecast, opts)}\n\n" +
            $"ПОДІЇ КАЛЕНДАРЯ (наступні 14 днів):\n{JsonSerializer.Serialize(c.UpcomingEvents, opts)}\n\n" +
            $"АКТИВНІ АКЦІЇ:\n{JsonSerializer.Serialize(c.ActivePromos, opts)}\n\n" +
            $"ДАНІ ПО ТОВАРАХ:\n{JsonSerializer.Serialize(c.StockLines, opts)}";
    }

    private const string ResponseSchemaJson = """
        {
          "type": "object",
          "properties": {
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "product_id": { "type": "string" },
                  "quantity_suggested": { "type": "number" },
                  "reasoning": { "type": "string" },
                  "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
                  "factors": {
                    "type": "object",
                    "properties": {
                      "weather": { "type": "number" },
                      "event": { "type": "number" },
                      "promo": { "type": "number" }
                    },
                    "required": ["weather", "event", "promo"],
                    "additionalProperties": false
                  }
                },
                "required": ["product_id", "quantity_suggested", "reasoning", "confidence", "factors"],
                "additionalProperties": false
              }
            }
          },
          "required": ["items"],
          "additionalProperties": false
        }
        """;

    /// <summary>Internal+static so unit tests can exercise parsing without an API call.</summary>
    internal static List<AiAdviceItem> ParseAdvice(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<AiAdviceItem>();

        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!Guid.TryParse(item.GetProperty("product_id").GetString(), out var productId))
                continue; // AI hallucinated an id — skip the line, base qty stays

            result.Add(new AiAdviceItem(
                productId,
                Math.Round(item.GetProperty("quantity_suggested").GetDecimal(), 2),
                item.GetProperty("reasoning").GetString() ?? "",
                item.GetProperty("confidence").GetString() ?? "medium",
                item.GetProperty("factors").GetRawText()));
        }

        return result;
    }
}
