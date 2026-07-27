using System.Globalization;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Ukrainian display copy + ordering for the three Фаза 2 enums, and the dynamic tier-range
/// label formatter (design doc §1.3: "Лейбл ('120-190 ₴') рахується динамічно через
/// PriceSegmentRangeFormatter.Format(key, boundaries)" — implemented here as
/// <see cref="RangeLabelUa"/> rather than a separate file, mirroring how
/// <see cref="MarketingAnalytics.RfmSegmentCatalog"/> keeps all of the RFM enum's display copy
/// in one place). Unlike RFM's fixed segment labels, price-tier ranges are per-tenant (boundaries
/// come from that tenant's own live quantiles) — never hardcoded currency figures here.
/// </summary>
public static class PriceSegmentCatalog
{
    /// <summary>All 7 tiers in ascending (lowest-spend-first) order — guarantees the comparison
    /// chart and all-time distribution always return exactly 7 points, including zero-count
    /// tiers, mirroring RfmSegmentCatalog.AllKeysInPriorityOrder's "never drop an empty bucket"
    /// guarantee.</summary>
    public static readonly IReadOnlyList<PriceSegmentKey> AllTiersInOrder =
    [
        PriceSegmentKey.Tier1, PriceSegmentKey.Tier2, PriceSegmentKey.Tier3, PriceSegmentKey.Tier4,
        PriceSegmentKey.Tier5, PriceSegmentKey.Tier6, PriceSegmentKey.Tier7,
    ];

    public static readonly IReadOnlyList<PriceAudienceKey> AllPriceAudiences =
    [
        PriceAudienceKey.RealGrowth, PriceAudienceKey.PriceGrowth, PriceAudienceKey.Declining, PriceAudienceKey.Stable,
    ];

    public static readonly IReadOnlyList<FrequencyAudienceKey> AllFrequencyAudiences =
    [
        FrequencyAudienceKey.Sleeping, FrequencyAudienceKey.Declining, FrequencyAudienceKey.Growing, FrequencyAudienceKey.Other,
    ];

    /// <summary>Maps a tier to its 1-based ordinal (matches the SQL CASE's <c>SegmentOrdinal</c>
    /// 1..7 and <see cref="PriceSegmentKey"/>'s own int backing — kept as an explicit named
    /// helper, rather than a raw cast, so repository code reads intentionally.</summary>
    public static int Ordinal(PriceSegmentKey key) => (int)key;

    public static PriceSegmentKey FromOrdinal(int ordinal) => ordinal switch
    {
        1 => PriceSegmentKey.Tier1, 2 => PriceSegmentKey.Tier2, 3 => PriceSegmentKey.Tier3,
        4 => PriceSegmentKey.Tier4, 5 => PriceSegmentKey.Tier5, 6 => PriceSegmentKey.Tier6, 7 => PriceSegmentKey.Tier7,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Price segment ordinal must be 1-7."),
    };

    /// <summary>"до 120 ₴" / "120-190 ₴" / "960+ ₴" — the exact three label shapes the competitor
    /// uses (analysis doc §5.2/§13), built from THIS tenant's live boundaries, never a fixed
    /// currency figure.</summary>
    public static string RangeLabelUa(PriceSegmentKey key, PriceSegmentBoundariesRow b) => key switch
    {
        PriceSegmentKey.Tier1 => $"до {Money(b.P20)} ₴",
        PriceSegmentKey.Tier2 => $"{Money(b.P20)}–{Money(b.P40)} ₴",
        PriceSegmentKey.Tier3 => $"{Money(b.P40)}–{Money(b.P60)} ₴",
        PriceSegmentKey.Tier4 => $"{Money(b.P60)}–{Money(b.P80)} ₴",
        PriceSegmentKey.Tier5 => $"{Money(b.P80)}–{Money(b.P90)} ₴",
        PriceSegmentKey.Tier6 => $"{Money(b.P90)}–{Money(b.P97)} ₴",
        PriceSegmentKey.Tier7 => $"{Money(b.P97)}+ ₴",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string Money(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    public static string LabelUa(PriceAudienceKey key) => key switch
    {
        PriceAudienceKey.RealGrowth => "Ростуть по-справжньому",
        PriceAudienceKey.PriceGrowth => "Ростуть через ціни",
        PriceAudienceKey.Declining => "Просідають",
        PriceAudienceKey.Stable => "Стабільні",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    public static string LabelUa(FrequencyAudienceKey key) => key switch
    {
        FrequencyAudienceKey.Sleeping => "Зовсім сплять",
        FrequencyAudienceKey.Declining => "Купують рідше",
        FrequencyAudienceKey.Growing => "Частота зросла",
        FrequencyAudienceKey.Other => "Інші / стабільні",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
