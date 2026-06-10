namespace ShelfGuard.Application.Features.Receipts.Dtos;

public sealed record ReceiptDto(
    Guid Id,
    Guid? SupplierId,
    string? SupplierName,
    Guid DestinationStoreId,
    string DestinationStoreName,
    bool ViaCentralStore,
    string Status,
    DateOnly? ExpectedAt,
    DateTime? ReceivedAt,
    string? Notes,
    DateTime CreatedAt,
    List<ReceiptItemDto> Items
);

public sealed record ReceiptItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductBarcode,
    decimal QuantityOrdered,
    decimal? QuantityReceived,
    decimal? PricePurchase,
    DateOnly? ExpiryDate,
    string? BatchNumber,
    string? DiscrepancyNotes,
    bool IsProcessed
);

public sealed record CreateReceiptRequest(
    Guid? SupplierId,
    Guid DestinationStoreId,
    bool ViaCentralStore,
    DateOnly? ExpectedAt,
    string? Notes,
    List<CreateReceiptItemRequest> Items
);

public sealed record CreateReceiptItemRequest(
    Guid ProductId,
    decimal QuantityOrdered,
    decimal? PricePurchase,
    DateOnly? ExpiryDate,
    string? BatchNumber
);

public sealed record UpdateReceiptItemRequest(
    decimal? QuantityReceived,
    decimal? PricePurchase,
    DateOnly? ExpiryDate,
    string? BatchNumber,
    string? DiscrepancyNotes
);

public sealed record UpdateItemsRequest(
    List<UpdateItemPayload> Items
);

public sealed record UpdateItemPayload(
    Guid ItemId,
    decimal? QuantityReceived,
    decimal? PricePurchase,
    DateOnly? ExpiryDate,
    string? BatchNumber,
    string? DiscrepancyNotes
);
