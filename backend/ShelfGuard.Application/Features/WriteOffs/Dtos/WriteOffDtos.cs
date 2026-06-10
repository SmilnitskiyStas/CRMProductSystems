namespace ShelfGuard.Application.Features.WriteOffs.Dtos;

public sealed record WriteOffDto(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string Status,
    string? Reason,
    decimal? TotalLossAmount,
    string? PdfUrl,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    List<WriteOffItemDto> Items
);

public sealed record WriteOffItemDto(
    Guid Id,
    Guid? ProductStockId,
    Guid ProductId,
    string ProductName,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? LossAmount
);

public sealed record CreateWriteOffRequest(
    Guid StoreId,
    string? Reason,
    string? Notes,
    List<CreateWriteOffItemRequest> Items
);

public sealed record CreateWriteOffItemRequest(
    Guid? ProductStockId,
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice
);
