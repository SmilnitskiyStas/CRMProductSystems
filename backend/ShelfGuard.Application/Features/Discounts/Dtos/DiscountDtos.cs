namespace ShelfGuard.Application.Features.Discounts.Dtos;

public sealed record DiscountDto(
    Guid      Id,
    Guid      TenantId,
    Guid      ProductId,
    Guid      StoreId,
    Guid?     ProductStockId,
    decimal   DiscountPercent,
    decimal?  PriceOriginal,
    decimal?  PriceDiscounted,
    string    Reason,
    DateTime  ValidFrom,
    DateTime? ValidUntil,
    string    Status,
    bool      AutoApplied,
    Guid?     CreatedBy,
    Guid?     ApprovedBy,
    DateTime  CreatedAt,
    DateTime? ApprovedAt,
    DateTime? WebhookSentAt
);

public sealed record CreateDiscountRequest(
    Guid     ProductId,
    Guid     StoreId,
    decimal  DiscountPercent,
    /// <summary>expiry | overstock | promo</summary>
    string   Reason          = "expiry",
    Guid?    ProductStockId  = null,
    decimal? PriceOriginal   = null,
    DateTime? ValidFrom      = null,
    DateTime? ValidUntil     = null
);

public sealed record DiscountListQuery(
    Guid?   StoreId = null,
    string? Status  = null
);
