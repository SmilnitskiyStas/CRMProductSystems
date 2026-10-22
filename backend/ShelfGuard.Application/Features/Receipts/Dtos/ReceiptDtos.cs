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
    List<ReceiptItemDto> Items,
    int PostponeCount = 0,
    DateOnly? OriginalExpectedAt = null,
    DateTime? PostponedAt = null,
    string? DelayReason = null,
    // TASK-755: "manual" | "auto_replenishment"; taken-by lock of an auto draft (null = untaken).
    string Source = "manual",
    Guid? TakenByUserId = null,
    string? TakenByName = null,
    DateTime? TakenAt = null
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

/// <summary>Move the expected delivery date of an open (not yet received) receipt to a later day.</summary>
public sealed record PostponeReceiptRequest(DateOnly NewExpectedAt, string? Reason);

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
    string? DiscrepancyNotes,
    // TASK-755: correct the ORDERED quantity of a draft (must be > 0). Null = leave unchanged.
    decimal? QuantityOrdered = null
);

/// <summary>Body of POST /api/receipts/auto-generate (TASK-755). Null/empty = every store the caller can see.</summary>
public sealed record AutoReplenishmentRequest(Guid? StoreLocationId);
