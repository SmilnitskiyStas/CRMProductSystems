using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.SupplierAdvisor;

/// <summary>
/// Claude-backed supplier recommendation advisor (v4-spec Phase 3, TASK-223).
/// Prompt template and provider wiring live here per the architecture rule:
/// AI integrations are isolated in Infrastructure/AI — never coupled to business logic.
///
/// Key resolution mirrors ClaudeOrderAdvisor:
///   tenant integration_configs row (service='claude', managed via Налаштування → Інтеграції)
///   → fallback to Claude:ApiKey env var.
/// </summary>
public sealed class SupplierAdvisor : ISupplierAdvisor
{
    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public SupplierAdvisor(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _envApiKey = config["Claude:ApiKey"];
        _defaultModel = config["Claude:Model"] ?? "claude-sonnet-4-6";
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        (await ResolveAsync(ct)).ApiKey is not null;

    private async Task<(string? ApiKey, string Model)> ResolveAsync(CancellationToken ct)
    {
        var row = await _db.IntegrationConfigs
            .Where(i => i.Service == "claude" && i.IsEnabled)
            .Select(i => i.Config)
            .FirstOrDefaultAsync(ct);

        if (row is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(row);
                var key   = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() : null;
                var model = doc.RootElement.TryGetProperty("model",   out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(key))
                    return (key, string.IsNullOrWhiteSpace(model) ? _defaultModel : model!);
            }
            catch (JsonException) { /* malformed config — fall through to env */ }
        }

        return (string.IsNullOrWhiteSpace(_envApiKey) ? null : _envApiKey, _defaultModel);
    }

    public async Task<SupplierRecommendationResult> RecommendAsync(
        SupplierRecommendationRequest request,
        IEnumerable<SupplierCandidateDto> candidates,
        CancellationToken ct = default)
    {
        var (apiKey, model) = await ResolveAsync(ct);
        if (apiKey is null)
            throw new InvalidOperationException(
                "Claude API key is not configured. Add it in Налаштування → Інтеграції → Claude AI.");

        var candidateList = candidates.ToList();
        var prompt = BuildUserPrompt(request, candidateList);

        var client = new AnthropicClient { ApiKey = apiKey };

        var parameters = new MessageCreateParams
        {
            Model      = model,
            MaxTokens  = 4096,
            System     = BuildSystemPrompt(),
            Messages   = [new() { Role = Role.User, Content = prompt }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = ResponseSchema() },
            },
        };

        var response = await client.Messages.Create(parameters, cancellationToken: ct);

        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Claude returned no text content.");

        var items = ParseRecommendations(text);
        var tokens = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        return new SupplierRecommendationResult(items, prompt, model, tokens);
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

    private static Dictionary<string, JsonElement> ResponseSchema()
    {
        const string schemaJson = """
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

        using var doc = JsonDocument.Parse(schemaJson);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

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
