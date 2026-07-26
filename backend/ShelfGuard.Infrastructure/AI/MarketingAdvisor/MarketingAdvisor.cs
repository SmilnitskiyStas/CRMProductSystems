using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.MarketingAdvisor;

/// <summary>
/// Claude-backed marketing advisor (TASK-406, plan §"Рекомендації — гібридний рушій" /
/// "AI-адвайзер"). Key resolution is a byte-for-byte copy of
/// <c>ClaudeOrderAdvisor.ResolveAsync</c>'s pattern per the task brief ("перевикористай ТОЧНО
/// той самий патерн резолву Claude-ключа") — tenant's integration_configs (service='claude') →
/// fallback to Claude:ApiKey env, RLS scopes the lookup to the caller's tenant. Deliberately a
/// SEPARATE advisor class (not a shared base with ClaudeOrderAdvisor/SupplierAdvisor/
/// BusinessAssistantAdvisor) — TASK-367's architecture audit already flagged the 3 existing
/// advisors' duplicated key-resolution logic as a candidate for extraction, but doing that
/// extraction is out of scope here; this file follows the existing (duplicated) shape rather
/// than inventing a 4th pattern.
/// </summary>
public sealed class MarketingAdvisor : IMarketingAdvisor
{
    // Same bound as ClaudeOrderAdvisor — a synchronous "Пояснити детальніше" click must not
    // block the request indefinitely.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public MarketingAdvisor(AppDbContext db, IConfiguration config)
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
                using var doc = System.Text.Json.JsonDocument.Parse(row);
                var key = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() : null;
                var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(key))
                    return (key, string.IsNullOrWhiteSpace(model) ? _defaultModel : model!);
            }
            catch (System.Text.Json.JsonException) { /* malformed config — fall through to env */ }
        }

        return (string.IsNullOrWhiteSpace(_envApiKey) ? null : _envApiKey, _defaultModel);
    }

    public async Task<MarketingAdvisorResult> ExplainAsync(MarketingAdvisorContext context, CancellationToken ct = default)
    {
        var (apiKey, model) = await ResolveAsync(ct);
        if (apiKey is null)
            throw new InvalidOperationException(
                "Claude API key is not configured. Add it in Налаштування → Інтеграції → Claude AI.");

        var client = new AnthropicClient { ApiKey = apiKey, Timeout = ApiTimeout };

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 1024,
            System = BuildSystemPrompt(),
            Messages = [new() { Role = Role.User, Content = BuildUserPrompt(context) }],
        };

        var response = await client.Messages.Create(parameters, cancellationToken: ct);

        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Claude returned no text content.");

        var tokens = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        return new MarketingAdvisorResult(text.Trim(), model, tokens);
    }

    private static string BuildSystemPrompt() =>
        "Ти — AI маркетинговий консультант для власника рітейл-бізнесу в Україні. " +
        "Тобі дають RFM-сегмент клієнтів із живими показниками та вже згенерований шаблонний " +
        "блок рекомендації (Тригер/Дія/Оффер/Застереження). Твоя задача — НЕ повторити шаблон, " +
        "а розкрити його детальніше: пояснити чому саме ці показники означають цей ризик/можливість, " +
        "дати 2-3 конкретні тактичні кроки на найближчий тиждень, врахувати сезонність і " +
        "специфіку українського рітейлу. Пиши українською, розмовно-професійним тоном, " +
        "3-5 речень, без markdown-заголовків і без повторення цифр, які вже показані користувачу " +
        "буквально — інтерпретуй їх, а не повторюй.";

    private static string BuildUserPrompt(MarketingAdvisorContext c) =>
        $"СЕГМЕНТ: {c.SegmentLabelUa} ({c.SegmentKey})\n" +
        $"Клієнтів: {c.CustomerCount} ({c.SharePercentOfPeriodCustomers:0.#}% покупців періоду)\n" +
        $"Частка обороту: {c.SharePercentOfPeriodRevenue:0.#}%\n" +
        $"Середня давність останньої покупки: {c.AverageRecencyDays:0.#} дн.\n" +
        $"Середній LTV (за весь час): {c.AverageLtv:N0} ₴\n" +
        $"Топ-товар: {c.TopProductName ?? "немає даних"}\n\n" +
        $"ШАБЛОННА РЕКОМЕНДАЦІЯ (уже показана користувачу):\n" +
        $"Тригер: {c.TemplateTriggerUa}\n" +
        $"Дія: {c.TemplateActionUa}\n" +
        $"Оффер: {c.TemplateOfferUa}\n" +
        $"Застереження: {c.TemplateCautionUa}\n\n" +
        "Розкрий це детальніше для власника бізнесу.";
}
