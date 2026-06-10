namespace ShelfGuard.Application.Features.Transfers.Dtos;

public sealed record TransferDto(
    Guid Id,
    Guid FromStoreId,
    string FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    string? TransferType,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    List<TransferItemDto> Items
);

public sealed record TransferItemDto(
    Guid Id,
    Guid ProductStockId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    DateOnly ExpiryDate,
    string? BatchNumber
);

public sealed record CreateTransferRequest(
    Guid FromStoreId,
    Guid ToStoreId,
    string? TransferType,
    string? Notes,
    List<CreateTransferItemRequest> Items
);

public sealed record CreateTransferItemRequest(
    Guid ProductStockId,
    decimal Quantity
);
