using ShelfGuard.Application.Features.Locations.Dtos;

namespace ShelfGuard.Application.Features.Locations;

/// <summary>Deterministic restock recommendation for a missing/low shelf position (no AI involved).</summary>
public static class ShelfRecommendationRules
{
    public const string None = "none";
    public const string RestockFromWarehouse = "restock_from_warehouse";
    public const string OrderFromSupplier = "order_from_supplier";
    public const string CheckManually = "check_manually";

    private const double LowConfidence = 0.4;

    /// <summary>Joins vision verdicts with insights and attaches quantities + a recommendation.</summary>
    public static List<ShelfScanItemDto> Enrich(
        IReadOnlyList<ShelfScanItemDto> items, IReadOnlyList<ShelfItemInsightDto> insights)
    {
        var byItem = insights.ToDictionary(i => i.ItemId);
        return items.Select(v =>
        {
            if (!byItem.TryGetValue(v.ItemId, out var ins)) return v;
            var rec = v.Status is "missing" or "low" ? Recommend(v, ins) : new ShelfRecommendationDto(None, 0, "low");
            return v with
            {
                OnShelfQty = ins.OnShelfQty, StoreQty = ins.StoreQty, WarehouseQty = ins.WarehouseQty,
                Sold7d = ins.Sold7d, Sold30d = ins.Sold30d, DaysOfCover = ins.DaysOfCover, Recommendation = rec,
            };
        }).ToList();
    }

    public static ShelfRecommendationDto Recommend(ShelfScanItemDto v, ShelfItemInsightDto ins)
    {
        var missing = v.Status == "missing";
        // Facings to restore, but at least ~2 days of sales when the item actually sells.
        var need = Math.Max(1, ins.ExpectedFacings - (missing ? 0 : v.EstimatedCount));
        var byDemand = ins.Adu > 0 ? (int)Math.Ceiling(ins.Adu * 2m) : 0;
        var qty = Math.Max(need, byDemand);

        string action;
        if (ins.WarehouseQty >= 1)
        {
            action = RestockFromWarehouse;
            qty = Math.Min(qty, (int)Math.Floor(ins.WarehouseQty));
        }
        else if (v.Confidence < LowConfidence || ins.OnShelfQty > 0)
        {
            // Vision unsure, or system says the shelf has stock while the photo does not: a human must look.
            action = CheckManually;
            qty = 0;
        }
        else
        {
            action = OrderFromSupplier;
        }

        return new ShelfRecommendationDto(action, qty, Priority(missing, ins));
    }

    private static string Priority(bool missing, ShelfItemInsightDto ins)
    {
        var level = missing ? 1 : 0; // 0 low, 1 medium, 2 high
        if (ins.Sold7d >= 7 || (ins.DaysOfCover is { } c && c <= 2)) level++;
        else if (ins.Sold7d == 0 && ins.Sold30d == 0) level--;
        return Math.Clamp(level, 0, 2) switch { 2 => "high", 1 => "medium", _ => "low" };
    }

    /// <summary>The scan reports only items that need action.</summary>
    public static bool NeedsAction(ShelfScanItemDto i) => i.Status is "missing" or "low";
}
