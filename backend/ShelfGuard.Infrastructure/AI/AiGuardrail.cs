using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Pure composition of the mandatory per-tenant AI isolation guardrail (managed-AI Phase 1;
/// per-slot variants since Phase 4). Kept free of I/O so it can be unit-tested directly (see
/// <c>AiGuardrailTests</c>); the DB lookups live in <c>AiPromptResolver</c>.
/// </summary>
public static class AiGuardrail
{
    /// <summary>
    /// Builds the guardrail prefix for <paramref name="slot"/>. Parameterised with the business
    /// name when known; the isolation rule applies either way. <see cref="AiSlot.Analyst"/> and
    /// <see cref="AiSlot.Assistant"/> share the internal-staff wording; <see cref="AiSlot.Consumer"/>
    /// gets a hardened customer-facing variant (Phase 4b).
    /// </summary>
    public static string Prefix(string? tenantName, AiSlot slot = AiSlot.Analyst)
    {
        var name = string.IsNullOrWhiteSpace(tenantName) ? null : tenantName!.Trim();
        var subject = name is null ? "цього бізнесу" : $"«{name}»";

        if (slot == AiSlot.Consumer)
            return
                $"Ти — AI-консультант у застосунку для КЛІЄНТІВ бізнесу {subject} (не для персоналу). " +
                "Відповідай лише про: власний бонусний рахунок і рівень лояльності цього клієнта, " +
                "його власні замовлення, публічний каталог і наявність товарів, правила програми " +
                "лояльності, адреси та графік роботи магазинів. Ніколи не розкривай внутрішні дані " +
                "бізнесу: закупівельні ціни, маржу, собівартість, постачальників, дані інших клієнтів, " +
                "обсяги продажів, залишки на складах, персонал, прогнози. На запити поза цим — " +
                "ввічливо відмовляй. Не вигадуй цифри, яких немає в наданому контексті.";

        return
            $"Ти працюєш виключно для бізнесу {subject} і оперуєш лише його власними даними. " +
            "Не надавай інформацію про інших клієнтів системи, про конкурентів, про ринок чи " +
            "галузеві порівняння та бенчмарки. На запити такого типу відповідай, що це поза " +
            $"межами твоєї допомоги — ти консультуєш лише по бізнесу {subject}. " +
            "(Постачальники, ринок постачання та власні дані бізнесу — дозволені теми.) " +
            "Не вигадуй зовнішніх цифр, яких немає в наданому контексті.";
    }

    /// <summary>
    /// Full system prompt = guardrail prefix (for <paramref name="slot"/>) + optional
    /// provider-set extra instructions + the advisor's own base prompt, in that order.
    /// </summary>
    public static string Compose(string? tenantName, string? extraInstructions, string basePrompt, AiSlot slot = AiSlot.Analyst)
    {
        var parts = new List<string>(3) { Prefix(tenantName, slot) };

        if (!string.IsNullOrWhiteSpace(extraInstructions))
            parts.Add(extraInstructions!.Trim());

        if (!string.IsNullOrWhiteSpace(basePrompt))
            parts.Add(basePrompt.Trim());

        return string.Join("\n\n", parts);
    }
}
