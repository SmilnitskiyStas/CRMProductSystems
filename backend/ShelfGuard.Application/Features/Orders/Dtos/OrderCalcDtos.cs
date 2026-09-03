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
    decimal InTransitFromMarketplace = 0m
);

public sealed record OrderCalcResult(
    Guid StoreId,
    DateTime CalculatedAt,
    int ProductsEvaluated,
    int LinesToOrder,
    List<OrderLineDto> Lines
);
