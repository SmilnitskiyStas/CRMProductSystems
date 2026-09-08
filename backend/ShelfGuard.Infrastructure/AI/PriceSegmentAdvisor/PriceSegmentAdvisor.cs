using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI.PriceSegmentAdvisor;

/// <summary>
/// Фаза 2 price/frequency-audience advisor (TASK-420). Provider-agnostic since managed-AI
/// Phase 2 — the model call goes through <see cref="IAiClientFactory"/> / <see cref="IAiChatClient"/>.
/// The per-tenant isolation guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
///
/// Handles all THREE explain flows (comparison price audience, all-time tier, frequency
/// audience) through the ONE shared <see cref="PriceSegmentAdvisorContext"/> shape.
/// </summary>
public sealed class PriceSegmentAdvisor : IPriceSegmentAdvisor
{
    private const AiSlot Slot = AiSlot.Assistant;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public PriceSegmentAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<PriceSegmentAdvisorResult> ExplainAsync(PriceSegmentAdvisorContext context, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), Slot, ct);
        var result = await client.CompleteAsync(new AiChatRequest(system, BuildUserPrompt(context), MaxTokens: 1024), ct);

        return new PriceSegmentAdvisorResult(result.Text.Trim(), result.Model, result.TokensUsed);
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
