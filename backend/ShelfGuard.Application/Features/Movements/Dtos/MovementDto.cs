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
    DateTime CreatedAt,
    Guid? PerformedById = null,
    string? PerformedByName = null,
    string? BatchNumber = null,
    DateOnly? BatchExpiryDate = null
);

/// <summary>
/// Supply that is on its way to a store but not yet in stock: an internal order (draft receipt)
/// or an open B2B marketplace order. <c>Source</c> is "receipt" | "marketplace".
/// </summary>
public sealed record IncomingSupplyDto(
    string Source,
    Guid Id,
    string? Number,
    string Status,
    decimal Quantity,
    string? Unit,
    DateTime OrderedAt,
    DateOnly? ExpectedAt,
    DateTime? ShippedAt,
    string? SupplierName,
    Guid? DestinationStoreId,
    string? DestinationStoreName
);

public sealed record MovementPageDto(
    IReadOnlyList<MovementDto> Items,
    int Total,
    int Page,
    int PageSize
);
