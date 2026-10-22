using System.Globalization;
using System.Text;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI.ShelfRestockAdvisor;

/// <summary>
/// Text-only advisor (slot <see cref="AiSlot.Analyst"/>): turns the deterministic restock lines of
/// a shelf scan into a short Ukrainian summary for the manager. The prompt goes through the same
/// mandatory per-tenant guardrail as the photo advisor. Returns null when the slot is not
/// configured; throws on provider failure (the caller treats that as "no summary").
/// </summary>
public sealed class ShelfRestockAdvisor : IShelfRestockAdvisor
{
    private const AiSlot Slot = AiSlot.Analyst;

    private const string SystemPrompt =
        "Ти — помічник менеджера магазину. Тобі дано перелік товарів, яких бракує на полиці, з залишками " +
        "(на полиці, у магазині, на складі), продажами за 7 днів і готовою рекомендацією (action: " +
        "restock_from_warehouse — викласти зі складу, order_from_supplier — замовити, check_manually — " +
        "перевірити вручну). Напиши короткий підсумок українською (до 4 речень): що найтерміновіше, " +
        "що викласти зі складу, що замовити. Не вигадуй даних і чисел, яких немає в переліку.";

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public ShelfRestockAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public async Task<string?> SummarizeAsync(IReadOnlyList<ShelfRestockLine> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0) return null;
        var client = await _ai.ResolveAsync(Slot, ct);
        if (client is null) return null;

        var system = await _prompt.WrapSystemPromptAsync(SystemPrompt, Slot, ct);
        var result = await client.CompleteAsync(new AiChatRequest(system, BuildUserPrompt(lines), MaxTokens: 400), ct);
        var text = result.Text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    internal static string BuildUserPrompt(IReadOnlyList<ShelfRestockLine> lines)
    {
        var sb = new StringBuilder("Товари, яких бракує на полиці:\n");
        foreach (var l in lines.Take(30))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"- \"{l.Name}\": {l.Status}, на полиці {l.OnShelfQty}, у магазині {l.StoreQty}, на складі {l.WarehouseQty}, " +
                $"продано за 7 днів {l.Sold7d}; рекомендація {l.Action}");
            if (l.SuggestedQty > 0) sb.Append(CultureInfo.InvariantCulture, $" {l.SuggestedQty} шт");
            sb.Append(CultureInfo.InvariantCulture, $", пріоритет {l.Priority}\n");
        }
        return sb.ToString();
    }
}
