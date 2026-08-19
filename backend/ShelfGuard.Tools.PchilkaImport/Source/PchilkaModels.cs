namespace ShelfGuard.Tools.PchilkaImport.Source;

/// <summary>One product from the Pchilka POS catalog, enriched with group/unit/barcode lookups.</summary>
public sealed class PchilkaProduct
{
    public required long ProductCode { get; init; }
    public required string Name { get; init; }
    public long? GroupCode { get; init; }
    public string? GroupName { get; init; }
    public string? UnitAbbr { get; init; }
    public decimal Vat { get; init; }
    public List<string> Barcodes { get; init; } = [];

    /// <summary>Ranking signal used to pick the top-N products — 30-day quantity sold.</summary>
    public decimal QtySold30d { get; init; }

    /// <summary>Net average per-unit selling price over the ranking window — used as Item.PriceRetail.</summary>
    public decimal AvgUnitPrice { get; init; }
}

public sealed class PchilkaOrderLine
{
    public required int LineNumber { get; init; }
    public required long ProductCode { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal LineTotal { get; init; }
    public decimal DiscountTotal { get; init; }
}

public sealed class PchilkaOrder
{
    public required int ShopCode { get; init; }
    public required int WorkplaceId { get; init; }
    public required long OrderCode { get; init; }
    public required DateOnly OrderDay { get; init; }
    public required DateTime OrderedAt { get; init; }
    public long? ClientCode { get; init; }
    public long? ReceiptNumber { get; init; }
    public decimal? OrderTotal { get; init; }
    public List<PchilkaOrderLine> Lines { get; } = [];

    /// <summary>Deterministic, globally-unique synthesized receipt number for ShelfGuard's own uniqueness constraint.</summary>
    public string SynthesizedReceiptNumber => $"PCH-{ShopCode}-{WorkplaceId}-{OrderCode}";
}
