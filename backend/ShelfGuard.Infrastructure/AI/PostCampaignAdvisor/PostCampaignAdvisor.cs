using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.PostCampaignAdvisor;

/// <summary>
/// Claude-backed post-campaign advisor (Фаза 4, TASK-472). Key resolution is a byte-for-byte
/// copy of <c>MarketingAdvisor.ResolveAsync</c>/<c>PriceSegmentAdvisor</c>'s own pattern (task
/// brief: "reuse this exact interface/pattern... do not build a new AI plumbing path") — tenant's
/// integration_configs (service='claude') → fallback to Claude:ApiKey env, RLS scopes the lookup
/// to the caller's tenant. A separate advisor class (not a shared base with the 5 existing Claude
/// advisors) for the same reason TASK-406/420 already gave: extracting the duplicated
/// key-resolution logic is a tracked, deliberately-deferred refactor (TASK-367), not something to
/// do as a side effect of adding a 6th advisor.
/// </summary>
public sealed class PostCampaignAdvisor : IPostCampaignAdvisor
{
    // Same bound as MarketingAdvisor/PriceSegmentAdvisor — a synchronous "Пояснити детальніше"
    // click must not block the request indefinitely.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public PostCampaignAdvisor(AppDbContext db, IConfiguration config)
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

    public async Task<PostCampaignAdvisorResult> ExplainAsync(PostCampaignAdvisorContext context, CancellationToken ct = default)
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

        return new PostCampaignAdvisorResult(text.Trim(), model, tokens);
    }

    private static string BuildSystemPrompt() =>
        "Ти — AI маркетинговий консультант для власника рітейл-бізнесу в Україні. " +
        "Тобі дають результати аналізу аудиторії ПІСЛЯ маркетингової кампанії: скільки клієнтів " +
        "повернулось (реактивовано), скільки залишилось активними (утримано), скільки перестало " +
        "купувати (відпали), і як змінився оборот проти періоду ДО кампанії. Тобі також дають вже " +
        "згенерований шаблонний блок рекомендації. Твоя задача — НЕ повторити шаблон, а розкрити " +
        "його детальніше: пояснити що ці показники означають для бізнесу, дати 2-3 конкретні " +
        "тактичні кроки на найближчий тиждень. ВАЖЛИВО: це аналіз \"до/після\" БЕЗ контрольної " +
        "групи — не стверджуй напряму, що зміна оборату спричинена саме кампанією, формулюй " +
        "обережно (\"це узгоджується з...\", а не \"кампанія спричинила...\"). Пиши українською, " +
        "розмовно-професійним тоном, 3-5 речень, без markdown-заголовків і без повторення цифр, " +
        "які вже показані користувачу буквально — інтерпретуй їх, а не повторюй.";

    private static string BuildUserPrompt(PostCampaignAdvisorContext c) =>
        $"СЕГМЕНТ: {c.TitleUa}\n" +
        $"Розмір сегмента: {c.MatchedCount}\n" +
        $"Реактивація: {(c.ReactivationRatePercent is { } rr ? $"{rr:0.#}%" : "н/д (не було неактивних до)")}\n" +
        $"Утримання: {(c.RetentionRatePercent is { } ret ? $"{ret:0.#}%" : "н/д (не було активних до)")}\n" +
        $"Зміна обороту: {(c.MoneyDeltaPercent is { } md ? $"{md:0.#}%" : "н/д (не було обороту до)")}\n" +
        (c.ExtraContextUa is null ? string.Empty : $"{c.ExtraContextUa}\n") +
        "\nШАБЛОННА РЕКОМЕНДАЦІЯ (уже показана користувачу):\n" +
        $"Тригер: {c.TemplateTriggerUa}\n" +
        $"Дія: {c.TemplateActionUa}\n" +
        $"Оффер: {c.TemplateOfferUa}\n" +
        $"Застереження: {c.TemplateCautionUa}\n\n" +
        "Розкрий це детальніше для власника бізнесу.";
}
