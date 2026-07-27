using System.Text.Json.Serialization;

namespace ShelfGuard.Domain.Constants;

/// <summary>
/// The 4 comparison-mode price audiences (Фаза 2 "Суми покупок" tab, TASK-420 design doc §1.6 /
/// docs/uployal/PRICE_SEGMENTS_ANALYSIS.md §7). <see cref="Stable"/> is the audience the
/// competitor deliberately never exposes as a real list/export (analysis §7.4/§25.3,
/// deep-cooking-nygaard.md brief item 6) — this codebase gives it full parity with the other
/// three (list/sort/paginate/export/recommendation) on purpose, per the brief.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PriceAudienceKey
{
    /// <summary>Segment rose AND items/receipt rose — "Ростуть по-справжньому".</summary>
    RealGrowth = 1,
    /// <summary>Segment rose but items/receipt did not — "Ростуть через ціни".</summary>
    PriceGrowth = 2,
    /// <summary>Segment fell — "Просідають".</summary>
    Declining = 3,
    /// <summary>Same segment both periods — "Стабільні" (no card/list/export in the competitor).</summary>
    Stable = 4,
}
