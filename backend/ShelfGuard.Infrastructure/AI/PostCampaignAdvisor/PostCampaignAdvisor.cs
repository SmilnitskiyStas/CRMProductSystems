using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI.PostCampaignAdvisor;

/// <summary>
/// Post-campaign advisor (Фаза 4, TASK-472). Provider-agnostic since managed-AI Phase 2 — the
/// model call goes through <see cref="IAiClientFactory"/> / <see cref="IAiChatClient"/>. The
/// per-tenant isolation guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
/// </summary>
public sealed class PostCampaignAdvisor : IPostCampaignAdvisor
{
    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public PostCampaignAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(ct);

    public async Task<PostCampaignAdvisorResult> ExplainAsync(PostCampaignAdvisorContext context, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), ct);
        var result = await client.CompleteAsync(new AiChatRequest(system, BuildUserPrompt(context), MaxTokens: 1024), ct);

        return new PostCampaignAdvisorResult(result.Text.Trim(), result.Model, result.TokensUsed);
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
