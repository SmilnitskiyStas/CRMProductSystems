using System.Text.Json.Serialization;

namespace ShelfGuard.Domain.Constants;

/// <summary>
/// The 4 "Частота та реактивація" audiences (Фаза 2, TASK-420 design doc §1.8 /
/// docs/uployal/PRICE_SEGMENTS_ANALYSIS.md §17). Population is the UNION of current-period and
/// previous-period buyers (never the intersection used by <see cref="PriceAudienceKey"/>'s
/// comparison cohort) — a <see cref="Sleeping"/> customer has, by definition, zero current-period
/// purchases, so an intersection would exclude them entirely.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FrequencyAudienceKey
{
    /// <summary>previous &gt; 0 AND current = 0 — "Зовсім сплять".</summary>
    Sleeping = 1,
    /// <summary>current &gt; 0 AND current &lt; previous AND decline% &gt;= threshold — "Купують рідше".</summary>
    Declining = 2,
    /// <summary>current &gt; previous (incl. previous = 0) — "Частота зросла".</summary>
    Growing = 3,
    /// <summary>Everything else in the union population — unchanged frequency, or a decline below
    /// threshold. No dedicated card in the competitor (analysis §17.5); still fully listable here.</summary>
    Other = 4,
}
