namespace ShelfGuard.Application.Features.Analytics.PromoNoSales;

public sealed record PromoNoSalesItemDto(
    Guid StoreId,
    string StoreName,
    Guid ProductId,
    string ProductName,
    string? Barcode,
    string PromoSource,
    Guid PromoSourceId,
    string PromoName,
    decimal? DiscountPercent,
    DateOnly PromoStart,
    DateOnly? PromoEnd,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int PeriodDays,
    decimal UnitsSold,
    decimal? ExpectedUnits,
    decimal StoreStock,
    decimal OnShelfStock,
    decimal WarehouseStock,
    Guid? ZoneId,
    string? ZoneName,
    /// <summary>no_sales | low_sales — the UI localizes the "possibly not displayed — run a revision" hint by it.</summary>
    string Reason);

public sealed record PromoNoSalesDto(
    DateOnly AsOf,
    int MinPromoDays,
    decimal MaxSalesRatio,
    IReadOnlyList<PromoNoSalesItemDto> Items);

/// <summary>Everything the detector needs for one (store, product, promo), loaded without N+1.</summary>
public sealed record PromoNoSalesCandidate(
    Guid StoreId,
    string StoreName,
    Guid ProductId,
    string ProductName,
    string? Barcode,
    PromoWindow Promo,
    decimal? DiscountPercent,
    decimal? Adu,
    decimal StoreStock,
    decimal OnShelfStock,
    decimal WarehouseStock,
    Guid? ZoneId,
    string? ZoneName,
    IReadOnlyDictionary<DateOnly, decimal> SalesByDate);
