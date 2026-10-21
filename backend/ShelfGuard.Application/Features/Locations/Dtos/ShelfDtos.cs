namespace ShelfGuard.Application.Features.Locations.Dtos;

public sealed record ShelfItemDto(
    Guid Id, Guid ShelfId, Guid ItemId, string ItemName, string? Barcode, int ExpectedFacings);

public sealed record ShelfSectionDto(Guid ShelfId, string Label, List<ShelfItemDto> Items);

public sealed record ShelfItemInput(Guid ItemId, int ExpectedFacings);

/// <summary>Replaces the whole planogram of one shelf section (items absent from the list are removed).</summary>
public sealed record SetShelfItemsRequest(Guid ShelfId, List<ShelfItemInput> Items);

/// <param name="Action">none | restock_from_warehouse | order_from_supplier | check_manually</param>
/// <param name="Priority">low | medium | high</param>
public sealed record ShelfRecommendationDto(string Action, int SuggestedQty, string Priority);

/// <param name="Status">present | low | missing</param>
/// <remarks>Quantity / sales / recommendation fields are filled by the insight enrichment (null on old scans).</remarks>
public sealed record ShelfScanItemDto(
    Guid ItemId, string Name, string Status, int EstimatedCount, double Confidence, string? Note,
    decimal? OnShelfQty = null, decimal? StoreQty = null, decimal? WarehouseQty = null,
    decimal? Sold7d = null, decimal? Sold30d = null, decimal? DaysOfCover = null,
    ShelfRecommendationDto? Recommendation = null);

/// <summary>Per-item product data for a shelf section: stock (shelf/store/warehouse), FEFO expiry, sales, cover, trend.</summary>
public sealed record ShelfItemInsightDto(
    Guid ItemId, string Name, string? Barcode, string? ImageUrl, int ExpectedFacings,
    decimal OnShelfQty, decimal StoreQty, decimal WarehouseQty, DateOnly? NearestExpiry,
    decimal Sold7d, decimal Sold30d, decimal Adu, decimal? DaysOfCover, decimal? TrendPct);

/// <param name="Status">completed | failed</param>
public sealed record ShelfScanDto(
    Guid Id,
    Guid LocationId,
    Guid ZoneId,
    Guid? ShelfId,
    string ImageUrl,
    string Status,
    int MissingCount,
    int LowCount,
    bool AlertPending,
    string? Summary,
    List<ShelfScanItemDto> Items,
    string? Model,
    string? ErrorMessage,
    Guid? CreatedBy,
    DateTime CreatedAt);

/// <summary>Stored in <c>shelf_scans.ResultJson</c> (camelCase).</summary>
public sealed record ShelfScanResult(string? Summary, List<ShelfScanItemDto> Items);
