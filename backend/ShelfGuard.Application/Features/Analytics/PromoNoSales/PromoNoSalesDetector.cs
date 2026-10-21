namespace ShelfGuard.Application.Features.Analytics.PromoNoSales;

/// <summary>
/// Thresholds for "item is on promo but does not sell while the store has stock". The worker job
/// (worker/src/services/promo-no-sales.ts) mirrors these numbers — change both together.
/// </summary>
public static class PromoNoSalesThresholds
{
    /// <summary>The promo must have run this many full days (yesterday and earlier) before judging.</summary>
    public const int MinPromoDays = 3;
    /// <summary>Only the last N days of a long-running promo are judged (bounds the data read).</summary>
    public const int LookbackDays = 30;
    /// <summary>Store stock (quantity) must be at least this to suspect "not on the shelf".</summary>
    public const decimal MinStoreStock = 1m;
    /// <summary>Flag when sold &lt; expected × this ratio ("no or almost no sales").</summary>
    public const decimal MaxSalesRatio = 0.2m;
    /// <summary>Below this expected volume the baseline is too noisy to judge.</summary>
    public const decimal MinExpectedUnits = 2m;
    /// <summary>Without any ADU baseline only a complete absence of sales over this many days is flagged.</summary>
    public const int NoBaselineMinDays = 7;
    /// <summary>Re-notify interval for a still-unresolved item.</summary>
    public const int RenotifyDays = 7;
}

public sealed record PromoWindow(string Source, Guid SourceId, string Name, DateOnly StartDate, DateOnly? EndDate)
{
    public string Key => $"{Source}:{SourceId}";
}

public sealed record PromoNoSalesVerdict(
    PromoWindow Promo,
    DateOnly From,
    DateOnly To,
    int Days,
    decimal UnitsSold,
    decimal? ExpectedUnits,
    string Reason);

public static class PromoNoSalesReasons
{
    public const string NoSales = "no_sales";
    public const string LowSales = "low_sales";
}

public static class PromoNoSalesDetector
{
    /// <summary>
    /// Judges one (store, product, promo). Returns null when there is nothing suspicious or not
    /// enough evidence. <paramref name="salesByDate"/> are DailySale quantities of this store+product.
    /// </summary>
    public static PromoNoSalesVerdict? Evaluate(
        PromoWindow promo, DateOnly today, decimal? adu, decimal storeStock,
        IReadOnlyDictionary<DateOnly, decimal> salesByDate)
    {
        if (storeStock < PromoNoSalesThresholds.MinStoreStock) return null;

        var yesterday = today.AddDays(-1);
        var end = promo.EndDate is { } e && e < yesterday ? e : yesterday;
        var start = promo.StartDate;
        var earliest = today.AddDays(-PromoNoSalesThresholds.LookbackDays);
        if (start < earliest) start = earliest;

        var days = end.DayNumber - start.DayNumber + 1;
        if (days < PromoNoSalesThresholds.MinPromoDays) return null;

        var sold = 0m;
        foreach (var (date, qty) in salesByDate)
            if (date >= start && date <= end) sold += qty;

        decimal? expected = adu is > 0 ? adu.Value * days : null;
        string reason;
        if (expected is null)
        {
            if (sold > 0 || days < PromoNoSalesThresholds.NoBaselineMinDays) return null;
            reason = PromoNoSalesReasons.NoSales;
        }
        else
        {
            if (expected < PromoNoSalesThresholds.MinExpectedUnits) return null;
            if (sold >= expected * PromoNoSalesThresholds.MaxSalesRatio) return null;
            reason = sold <= 0 ? PromoNoSalesReasons.NoSales : PromoNoSalesReasons.LowSales;
        }

        return new PromoNoSalesVerdict(promo, start, end, days, sold, expected is null ? null : Math.Round(expected.Value, 1), reason);
    }
}
