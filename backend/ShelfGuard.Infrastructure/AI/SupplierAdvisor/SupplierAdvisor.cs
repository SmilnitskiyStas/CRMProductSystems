using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI.SupplierAdvisor;

/// <summary>
/// Supplier recommendation advisor (v4-spec Phase 3, TASK-223). Prompt template lives here per
/// the architecture rule; the model call is provider-agnostic since managed-AI Phase 2 —
/// through <see cref="IAiClientFactory"/> / <see cref="IAiChatClient"/>. Structured output.
/// The per-tenant isolation guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
/// </summary>
public sealed class SupplierAdvisor : ISupplierAdvisor
{
    private const AiSlot Slot = AiSlot.Analyst;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public SupplierAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<SupplierRecommendationResult> RecommendAsync(
        SupplierRecommendationRequest request,
        IEnumerable<SupplierCandidateDto> candidates,
        CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var prompt = BuildUserPrompt(request, candidates.ToList());
        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), Slot, ct);
        var result = await client.CompleteAsync(
            new AiChatRequest(system, prompt, MaxTokens: 4096, JsonSchema: ResponseSchemaJson), ct);

        var items = ParseRecommendations(result.Text);
        return new SupplierRecommendationResult(items, prompt, result.Model, result.TokensUsed);
    }

    // ── Prompts ───────────────────────────────────────────────────────────────

    private static string BuildSystemPrompt() =>
        "Ти — AI асистент з управління закупівлями для роздрібного бізнесу в Україні. " +
        "Твоє завдання — проаналізувати список постачальників і потребу в товарі, " +
        "та порекомендувати найкращих постачальників у вигляді ранжованого списку. " +
        "Враховуй рейтинг, швидкість доставки, точність замовлень та відповідність товару. " +
        "Обґрунтування пиши українською мовою, коротко (1-2 речення). " +
        "Відповідай ТІЛЬКИ JSON за заданою схемою.";

    internal static string BuildUserPrompt(
        SupplierRecommendationRequest request,
        IReadOnlyList<SupplierCandidateDto> candidates)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        return
            $"ПОТРЕБА В ТОВАРІ:\n" +
            $"  Товар: {request.ItemName}\n" +
            $"  Регіон: {request.Region ?? "не вказано"}\n" +
            $"  Кількість: {request.RequiredQty?.ToString() ?? "не вказано"}\n" +
            $"  Примітка: {request.Notes ?? "немає"}\n\n" +
            $"ПОСТАЧАЛЬНИКИ (JSON):\n{JsonSerializer.Serialize(candidates, opts)}\n\n" +
            $"Поверни JSON масив recommendations з топ 3-5 постачальниками, відсортованими за придатністю.";
    }

    private const string ResponseSchemaJson = """
        {
          "type": "object",
          "properties": {
            "recommendations": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "supplier_id":         { "type": "string" },
                  "rank":                { "type": "number" },
                  "score":               { "type": "number" },
                  "reasoning":           { "type": "string" },
                  "matched_item_name":   { "type": "string" },
                  "matched_item_price":  { "type": "number" }
                },
                "required": ["supplier_id", "rank", "score", "reasoning"],
                "additionalProperties": false
              }
            }
          },
          "required": ["recommendations"],
          "additionalProperties": false
        }
        """;

    /// <summary>Internal + static so unit tests can exercise parsing without an API call.</summary>
    internal static List<SupplierRecommendationItem> ParseRecommendations(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<SupplierRecommendationItem>();

        foreach (var item in doc.RootElement.GetProperty("recommendations").EnumerateArray())
        {
            if (!Guid.TryParse(item.GetProperty("supplier_id").GetString(), out var supplierId))
                continue; // AI hallucinated an id — skip

            decimal? price = null;
            if (item.TryGetProperty("matched_item_price", out var priceEl) &&
                priceEl.ValueKind == JsonValueKind.Number)
                price = priceEl.GetDecimal();

            string? matchedName = null;
            if (item.TryGetProperty("matched_item_name", out var nameEl) &&
                nameEl.ValueKind == JsonValueKind.String)
                matchedName = nameEl.GetString();

            result.Add(new SupplierRecommendationItem(
                supplierId,
                item.GetProperty("rank").GetInt32(),
                Math.Round(item.GetProperty("score").GetDecimal(), 4),
                item.GetProperty("reasoning").GetString() ?? "",
                matchedName,
                price));
        }

        return result;
    }
}
