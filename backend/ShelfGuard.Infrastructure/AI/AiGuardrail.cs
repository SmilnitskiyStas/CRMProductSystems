namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Pure composition of the mandatory per-tenant AI isolation guardrail (managed-AI Phase 1).
/// Kept free of I/O so it can be unit-tested directly (see <c>AiGuardrailTests</c>); the DB
/// lookups live in <c>AiPromptResolver</c>.
/// </summary>
public static class AiGuardrail
{
    /// <summary>
    /// Builds the guardrail prefix. Parameterised with the business name when known; the
    /// isolation rule applies either way.
    /// </summary>
    public static string Prefix(string? tenantName)
    {
        var name = string.IsNullOrWhiteSpace(tenantName) ? null : tenantName!.Trim();
        var subject = name is null ? "цього бізнесу" : $"«{name}»";

        return
            $"Ти працюєш виключно для бізнесу {subject} і оперуєш лише його власними даними. " +
            "Не надавай інформацію про інших клієнтів системи, про конкурентів, про ринок чи " +
            "галузеві порівняння та бенчмарки. На запити такого типу відповідай, що це поза " +
            $"межами твоєї допомоги — ти консультуєш лише по бізнесу {subject}. " +
            "(Постачальники, ринок постачання та власні дані бізнесу — дозволені теми.) " +
            "Не вигадуй зовнішніх цифр, яких немає в наданому контексті.";
    }

    /// <summary>
    /// Full system prompt = guardrail prefix + optional provider-set extra instructions +
    /// the advisor's own base prompt, in that order.
    /// </summary>
    public static string Compose(string? tenantName, string? extraInstructions, string basePrompt)
    {
        var parts = new List<string>(3) { Prefix(tenantName) };

        if (!string.IsNullOrWhiteSpace(extraInstructions))
            parts.Add(extraInstructions!.Trim());

        if (!string.IsNullOrWhiteSpace(basePrompt))
            parts.Add(basePrompt.Trim());

        return string.Join("\n\n", parts);
    }
}
