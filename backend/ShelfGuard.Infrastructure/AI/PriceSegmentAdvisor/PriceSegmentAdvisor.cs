using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.PriceSegmentAdvisor;

/// <summary>
/// Claude-backed Фаза 2 advisor (TASK-420, design doc §0: "5-й адвайзер, той самий патерн
/// резолву Claude-ключа, що вже в ClaudeOrderAdvisor.cs/MarketingAdvisor.cs"). Key resolution is
/// a byte-for-byte copy of <c>MarketingAdvisor.ResolveAsync</c>'s pattern — tenant's
/// integration_configs (service='claude') → fallback to Claude:ApiKey env, RLS scopes the lookup
/// to the caller's tenant. Deliberately a separate advisor class, not a shared base with the 4
/// existing advisors — same reasoning MarketingAdvisor's own doc comment already gives (a real
/// extraction candidate per TASK-367, but out of scope for this task).
///
/// Handles all THREE Фаза 2 explain flows (comparison price audience, all-time tier, frequency
/// audience) through the ONE shared <see cref="PriceSegmentAdvisorContext"/> shape — see that
/// record's own doc for why a single context generalizes across all three.
/// </summary>
public sealed class PriceSegmentAdvisor : IPriceSegmentAdvisor
{
    // Same bound as MarketingAdvisor/ClaudeOrderAdvisor — a synchronous "Пояснити детальніше"
    // click must not block the request indefinitely.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public PriceSegmentAdvisor(AppDbContext db, IConfiguration config)
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

    public async Task<PriceSegmentAdvisorResult> ExplainAsync(PriceSegmentAdvisorContext context, CancellationToken ct = default)
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

        return new PriceSegmentAdvisorResult(text.Trim(), model, tokens);
    }

    private static string BuildSystemPrompt() =>
        "Ти — AI маркетинговий консультант для власника рітейл-бізнесу в Україні. " +
        "Тобі дають цінову або частотну аудиторію покупців (перехід між ціновими сегментами, " +
        "або зміна частоти покупок) із живими показниками та вже згенерований шаблонний блок " +
        "рекомендації (Тригер/Дія/Оффер/Застереження). Твоя задача — НЕ повторити шаблон, а " +
        "розкрити його детальніше: пояснити чому саме ці показники означають цей ризик/можливість, " +
        "дати 2-3 конкретні тактичні кроки на найближчий тиждень, врахувати сезонність і специфіку " +
        "українського рітейлу. Пиши українською, розмовно-професійним тоном, 3-5 речень, без " +
        "markdown-заголовків і без повторення цифр, які вже показані користувачу буквально — " +
        "інтерпретуй їх, а не повторюй.";

    private static string BuildUserPrompt(PriceSegmentAdvisorContext c)
    {
        var text =
            $"АУДИТОРІЯ: {c.TitleUa}\n" +
            $"Клієнтів: {c.CustomerCount}" +
            (c.SharePercent is { } sp ? $" ({sp:0.#}% бази порівняння)" : string.Empty) + "\n" +
            (c.AverageLtv is { } ltv ? $"Середній LTV (за весь час): {ltv:N0} ₴\n" : string.Empty) +
            (c.ExtraContextUa is { } extra ? $"{extra}\n" : string.Empty) +
            "\nШАБЛОННА РЕКОМЕНДАЦІЯ (уже показана користувачу):\n" +
            $"Тригер: {c.TemplateTriggerUa}\n" +
            $"Дія: {c.TemplateActionUa}\n" +
            $"Оффер: {c.TemplateOfferUa}\n" +
            $"Застереження: {c.TemplateCautionUa}\n\n" +
            "Розкрий це детальніше для власника бізнесу.";
        return text;
    }
}
