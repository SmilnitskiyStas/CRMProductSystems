using System.Globalization;
using System.Text;
using System.Text.Json;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.AI.ShelfPhotoAdvisor;

/// <summary>
/// Vision advisor (slot <see cref="AiSlot.Analyst"/>): compares a shelf photo with the zone
/// planogram and returns, per expected item, present | low | missing + estimated facing count +
/// confidence. Structured JSON output; system prompt goes through the mandatory per-tenant
/// guardrail (<see cref="IAiPromptResolver"/> / <see cref="AiGuardrail"/>). Invoked only on an
/// explicit user upload; the result is advisory and never written to product_stock.
/// </summary>
public sealed class ShelfPhotoAdvisor : IShelfPhotoAdvisor
{
    private const AiSlot Slot = AiSlot.Analyst;

    internal const string Schema = """
    {
      "type": "object",
      "properties": {
        "items": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "itemId": { "type": "string" },
              "status": { "type": "string", "enum": ["present", "low", "missing"] },
              "estimatedCount": { "type": "integer" },
              "confidence": { "type": "number" },
              "note": { "type": "string" }
            },
            "required": ["itemId", "status", "estimatedCount", "confidence", "note"],
            "additionalProperties": false
          }
        },
        "summary": { "type": "string" }
      },
      "required": ["items", "summary"],
      "additionalProperties": false
    }
    """;

    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public ShelfPhotoAdvisor(IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(Slot, ct);

    public async Task<ShelfPhotoAnalysis> AnalyzeAsync(
        byte[] image, string mediaType, IReadOnlyList<ShelfPhotoExpectedItem> expected, CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(Slot, ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var system = await _prompt.WrapSystemPromptAsync(SystemPrompt, Slot, ct);
        var result = await client.CompleteAsync(
            new AiChatRequest(system, BuildUserPrompt(expected), MaxTokens: 2048, JsonSchema: Schema,
                Images: BuildImages(image, mediaType, expected)), ct);

        return Parse(result.Text, expected, result.Model, result.TokensUsed);
    }

    /// <summary>Reference photos (each labelled with item name/id/barcode) first, then the shelf photo to analyse.</summary>
    internal static List<AiImage> BuildImages(byte[] shelf, string mediaType, IReadOnlyList<ShelfPhotoExpectedItem> expected)
    {
        var images = new List<AiImage>();
        foreach (var e in expected.Where(e => e.References is { Count: > 0 }))
        {
            var barcode = string.IsNullOrWhiteSpace(e.Barcode) ? string.Empty : $", barcode {e.Barcode}";
            var refs = e.References!;
            for (var i = 0; i < refs.Count; i++)
            {
                var angle = refs.Count > 1 ? $" {i + 1}/{refs.Count}" : string.Empty;
                var side = string.IsNullOrWhiteSpace(refs[i].Label) ? string.Empty : $" [{refs[i].Label}]";
                images.Add(new AiImage(refs[i].Data, refs[i].MediaType,
                    $"Reference photo{angle}{side} of item \"{e.Name}\" (id {e.ItemId}{barcode}):"));
            }
        }
        images.Add(new AiImage(shelf, mediaType, "SHELF PHOTO TO ANALYSE (the photo of the actual shelf):"));
        return images;
    }

    private const string SystemPrompt =
        "Ти — AI-аудитор викладки товарів у магазині. Спершу йдуть еталонні фото деяких товарів зі списку " +
        "(кожне з підписом — назва та itemId), останнім — ФОТО ПОЛИЦІ для аналізу. Впізнавай товари зі списку " +
        "на фото полиці, порівнюючи з еталонним фото, упаковкою та назвою; для товарів без еталонного фото " +
        "орієнтуйся на назву/штрихкод. На фото полиці — полиця. Тобі дано список товарів, " +
        "які за планограмою мають стояти на цій полиці, з очікуваною кількістю фейсів (фейс — одна " +
        "одиниця товару в ряду на фронті полиці). Для КОЖНОГО товару зі списку визнач: " +
        "present — товар видно в достатній кількості (не менше очікуваних фейсів); " +
        "low — товар є, але фейсів помітно менше за очікувані; " +
        "missing — товару на фото немає. estimatedCount — твоя оцінка кількості фейсів, яку видно " +
        "на фото (0 якщо missing). confidence — впевненість від 0 до 1. Якщо фото нечітке або товар " +
        "неможливо розпізнати — став нижчу confidence і поясни в note. Не вигадуй товарів поза " +
        "списком. Поверни рівно один запис на кожен товар зі списку, з тим самим itemId. " +
        "note — коротко українською.";

    private static string BuildUserPrompt(IReadOnlyList<ShelfPhotoExpectedItem> expected)
    {
        var sb = new StringBuilder("Очікувані товари на полиці:\n");
        foreach (var e in expected)
        {
            sb.Append(CultureInfo.InvariantCulture, $"- itemId={e.ItemId}; назва=\"{e.Name}\"");
            if (!string.IsNullOrWhiteSpace(e.Barcode)) sb.Append(CultureInfo.InvariantCulture, $"; штрихкод={e.Barcode}");
            sb.Append(CultureInfo.InvariantCulture, $"; очікувано фейсів={e.ExpectedFacings}\n");
        }
        sb.Append("\nПорівняй з фото і поверни JSON за схемою.");
        return sb.ToString();
    }

    /// <summary>Internal for unit tests: maps the model's JSON to verdicts, one per expected item.</summary>
    internal static ShelfPhotoAnalysis Parse(
        string json, IReadOnlyList<ShelfPhotoExpectedItem> expected, string model, int tokens)
    {
        var reported = new Dictionary<Guid, ShelfPhotoItemVerdict>();
        string? summary = null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
            summary = s.GetString();

        if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (!Guid.TryParse(el.TryGetProperty("itemId", out var id) ? id.GetString() : null, out var itemId))
                    continue;
                var exp = expected.FirstOrDefault(e => e.ItemId == itemId);
                if (exp is null) continue; // ignore hallucinated items

                var status = el.TryGetProperty("status", out var st) ? st.GetString() : null;
                if (status is not ("present" or "low" or "missing")) status = "missing";
                var count = el.TryGetProperty("estimatedCount", out var c) && c.TryGetInt32(out var ci) ? Math.Max(0, ci) : 0;
                var conf = el.TryGetProperty("confidence", out var cf) && cf.TryGetDouble(out var cd)
                    ? Math.Clamp(cd, 0, 1) : 0;
                var note = el.TryGetProperty("note", out var n) ? n.GetString() : null;

                reported[itemId] = new ShelfPhotoItemVerdict(itemId, exp.Name, status, count, conf, note);
            }
        }

        // One verdict per expected item: anything the model skipped is reported as missing with
        // zero confidence so the UI can flag it "needs check" rather than silently dropping it.
        var items = expected
            .Select(e => reported.TryGetValue(e.ItemId, out var v)
                ? v
                : new ShelfPhotoItemVerdict(e.ItemId, e.Name, "missing", 0, 0, "AI не повернув оцінку для цього товару."))
            .ToList();

        return new ShelfPhotoAnalysis(items, summary, model, tokens);
    }
}
