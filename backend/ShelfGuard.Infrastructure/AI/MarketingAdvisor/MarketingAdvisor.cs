using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI.MarketingAdvisor;

/// <summary>
/// Marketing advisor (TASK-406). Provider-agnostic since managed-AI Phase 2 — the model call
/// goes through <see cref="IAiClientFactory"/> / <see cref="IAiChatClient"/> (Claude or an
/// OpenAI-compatible endpoint, per the tenant's provider config). The mandatory per-tenant
/// isolation guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
/// </summary>
public sealed class MarketingAdvisor : IMarketingAdvisor
{
    private const AiSlot Slot = AiSlot.Assistant;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public MarketingAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<MarketingAdvisorResult> ExplainAsync(MarketingAdvisorContext context, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), Slot, ct);
        var result = await client.CompleteAsync(new AiChatRequest(system, BuildUserPrompt(context), MaxTokens: 1024), ct);

        return new MarketingAdvisorResult(result.Text.Trim(), result.Model, result.TokensUsed);
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
