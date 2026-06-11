namespace ShelfGuard.Application.Features.Orders.Dtos;

public sealed record CalculateOrderRequest(Guid StoreId);

public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    string? Barcode,
    decimal BufferTotal,
    decimal SafetyBuffer,
    decimal StockOnHand,
    decimal InTransit,
    decimal QuantityRaw,
    decimal QuantityToOrder,
    decimal Moq,
    decimal Usq,
    string Rounding // none | moq_floor | usq_rounded
);

public sealed record OrderCalcResult(
    Guid StoreId,
    DateTime CalculatedAt,
    int ProductsEvaluated,
    int LinesToOrder,
    List<OrderLineDto> Lines
);
