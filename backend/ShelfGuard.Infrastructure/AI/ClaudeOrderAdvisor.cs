using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Claude-backed order advisor (v2-spec §7). Prompt template and provider wiring live
/// here per the architecture rule: AI integrations never couple to business logic.
/// Uses structured outputs so the response is guaranteed-valid JSON.
/// Key resolution: tenant's integration_configs (service='claude', managed via web UI)
/// → fallback to Claude:ApiKey env. RLS scopes the lookup to the caller's tenant.
/// </summary>
public sealed class ClaudeOrderAdvisor : IAiOrderAdvisor
{
    // SDK default is 10 minutes (retried up to 3x by default => worst case ~30 min blocking
    // an inline HTTP request from a manager clicking "Generate"). Bounded to something a
    // synchronous POST /api/ai-orders/generate call can reasonably wait on — Block 7 audit.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public ClaudeOrderAdvisor(AppDbContext db, IConfiguration config)
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
                var key = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() : null;
                var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(key))
                    return (key, string.IsNullOrWhiteSpace(model) ? _defaultModel : model!);
            }
            catch (JsonException) { /* malformed config — fall through to env */ }
        }

        return (string.IsNullOrWhiteSpace(_envApiKey) ? null : _envApiKey, _defaultModel);
    }

    public async Task<AiAdviceResult> AdviseAsync(AiOrderContext context, CancellationToken ct = default)
    {
        var (apiKey, model) = await ResolveAsync(ct);
        if (apiKey is null)
            throw new InvalidOperationException(
                "Claude API key is not configured. Add it in Налаштування → Інтеграції → Claude AI.");

        var client = new AnthropicClient { ApiKey = apiKey, Timeout = ApiTimeout };

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 8192,
            System = BuildSystemPrompt(),
            Messages = [new() { Role = Role.User, Content = BuildUserPrompt(context) }],
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

        var items = ParseAdvice(text);
        var tokens = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        return new AiAdviceResult(items, model, tokens);
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

    private static Dictionary<string, JsonElement> ResponseSchema()
    {
        const string schemaJson = """
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

        using var doc = JsonDocument.Parse(schemaJson);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

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
