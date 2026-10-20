using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShelfGuard.Application.Features.Promotions;

public static class PromotionRuleTypes
{
    public const string PercentOff = "percent_off";
    public const string FixedOff = "fixed_off";
    public const string FixedPrice = "fixed_price";
    public const string BuyNGetM = "buy_n_get_m";
}

public static class PromotionMatchModes
{
    public const string PerItem = "per_item";
    public const string AcrossSet = "across_set";
}

public static class PromotionAudienceKinds
{
    public const string All = "all";
    public const string LoyaltyMember = "loyalty_member";
    public const string LoyaltyTier = "loyalty_tier";
}

/// <summary>
/// Rule definition stored as JSON in promotion_rule_versions.DefinitionJson and synced verbatim
/// to the cashier. Shape (camelCase) = .claude/docs/promotion-rules.md "Rule definition".
/// </summary>
public sealed class PromotionDefinition
{
    public string Type { get; set; } = string.Empty;
    public PromotionTargets Targets { get; set; } = new();
    public PromotionScope Scope { get; set; } = new();
    public PromotionAudience Audience { get; set; } = new();
    public PromotionParams Params { get; set; } = new();
    public PromotionLimits Limits { get; set; } = new();
    public bool ExcludeAlreadyDiscounted { get; set; }
    /// <summary>Tie-break only (equal customer price): LOWER number wins, then the older rule.</summary>
    public int Priority { get; set; } = 100;
}

public sealed class PromotionTargets
{
    public List<Guid> ItemIds { get; set; } = [];
    public List<Guid> CategoryIds { get; set; } = [];
}

public sealed class PromotionScope
{
    /// <summary>Empty = every store of the tenant.</summary>
    public List<Guid> StoreIds { get; set; } = [];
    /// <summary>Inclusive start (UTC instant). Null = no lower bound.</summary>
    public DateTimeOffset? ValidFrom { get; set; }
    /// <summary>Exclusive end (UTC instant). Null = open-ended.</summary>
    public DateTimeOffset? ValidTo { get; set; }
    /// <summary>ISO weekdays 1 (Mon) .. 7 (Sun) of the store-local date. Empty = every day.</summary>
    public List<int> Weekdays { get; set; } = [];
    /// <summary>"HH:mm" store-local, inclusive. Optional.</summary>
    public string? TimeFrom { get; set; }
    /// <summary>"HH:mm" store-local, exclusive. TimeFrom &gt; TimeTo = window over midnight.</summary>
    public string? TimeTo { get; set; }
}

public sealed class PromotionAudience
{
    public string Kind { get; set; } = PromotionAudienceKinds.All;
    public List<Guid> TierIds { get; set; } = [];
}

public sealed class PromotionParams
{
    public decimal? Percent { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Price { get; set; }
    public int? BuyQty { get; set; }
    public int? GetQty { get; set; }
    public decimal? GetPrice { get; set; }
    public decimal? GetPercent { get; set; }
    public string? MatchMode { get; set; }
}

public sealed class PromotionLimits
{
    /// <summary>Null = unlimited. One application = one discounted unit (simple types) or one
    /// buy+get group (buy_n_get_m). 0 = the rule never applies.</summary>
    public int? MaxApplicationsPerReceipt { get; set; }
}

public static class PromotionDefinitionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Null when the JSON is malformed (the engine then simply ignores the rule).</summary>
    public static PromotionDefinition? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PromotionDefinition>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(PromotionDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    /// <summary>"HH:mm" (or "HH:mm:ss") → time of day; null when absent or malformed.</summary>
    public static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return TimeOnly.TryParseExact(value.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t)
            ? t.ToTimeSpan()
            : null;
    }
}
