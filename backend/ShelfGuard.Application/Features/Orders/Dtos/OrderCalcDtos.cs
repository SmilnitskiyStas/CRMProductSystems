namespace ShelfGuard.Application.Features.Orders.Dtos;

public sealed record CalculateOrderRequest(Guid StoreId);

public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    string? Barcode,
    decimal BufferTotal,
    decimal BufferGreen,
    decimal BufferYellow,
    decimal BufferRed,
    decimal SafetyBuffer,
    decimal StockOnHand,
    decimal InTransit,
    decimal QuantityRaw,
    decimal EventCoefficient,
    decimal WeatherCoefficient,
    decimal PromoCoefficient,
    decimal QuantityToOrder,
    decimal Moq,
    decimal Usq,
    string Rounding, // none | moq_floor | usq_rounded
    /// <summary>
    /// How much of <see cref="InTransit"/> comes from open B2B marketplace orders headed to this
    /// store (Phase 4, plan D5) — an order the buyer already placed with a supplier but has not
    /// yet received. <see cref="InTransit"/> is the combined figure the formula subtracts
    /// (draft supplier receipts + this); this field is the marketplace slice, broken out so the
    /// order-review UI can show the source of the "in transit" number in a tooltip. Always ≤
    /// <see cref="InTransit"/>. Unit-mismatched marketplace lines are excluded (see the repo).
    /// </summary>
    decimal InTransitFromMarketplace = 0m,
    /// <summary>The product's default supplier — used to split a confirmed order into one receipt per supplier.</summary>
    Guid? SupplierId = null,
    /// <summary>Catalog purchase price (per unit) — prefill for the confirmed order lines; may be null.</summary>
    decimal? PricePurchase = null,
    /// <summary>Part of <see cref="InTransit"/> whose delivery is late (past its date or postponed). Still counted as in transit.</summary>
    decimal InTransitDelayed = 0m,
    /// <summary>Latest expected delivery date among the delayed in-transit orders.</summary>
    DateOnly? DelayedUntil = null,
    /// <summary>True when at least one of those orders was postponed on purpose.</summary>
    bool DelayPostponed = false
);

public sealed record OrderCalcResult(
    Guid StoreId,
    DateTime CalculatedAt,
    int ProductsEvaluated,
    int LinesToOrder,
    List<OrderLineDto> Lines
);
