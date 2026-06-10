namespace ShelfGuard.Application.Features.Movements.Dtos;

public sealed record MovementDto(
    Guid Id,
    string MovementType,
    Guid ProductId,
    string? ProductName,
    Guid? FromStoreId,
    string? FromStoreName,
    Guid? ToStoreId,
    string? ToStoreName,
    decimal Quantity,
    decimal? QuantityBefore,
    decimal? QuantityAfter,
    decimal? UnitPrice,
    decimal? TotalAmount,
    Guid? ReferenceId,
    string? ReferenceType,
    string? Notes,
    DateTime CreatedAt
);

public sealed record MovementPageDto(
    IReadOnlyList<MovementDto> Items,
    int Total,
    int Page,
    int PageSize
);
