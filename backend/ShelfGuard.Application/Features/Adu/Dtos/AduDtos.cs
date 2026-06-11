namespace ShelfGuard.Application.Features.Adu.Dtos;

public sealed record AduDto(
    Guid StoreId,
    Guid ProductId,
    string? ProductName,
    decimal? Adu30d,
    decimal? Adu60d,
    decimal? Adu90d,
    decimal? AduEffective,
    short? ProductGroup,
    int? ValidDays30d,
    int? ValidDays60d,
    DateTime CalculatedAt
);

public sealed record RecalculateRequest(Guid StoreId);

public sealed record RecalculateResult(
    Guid StoreId,
    int ProductsProcessed,
    int WithEffectiveAdu,
    int InsufficientData
);
