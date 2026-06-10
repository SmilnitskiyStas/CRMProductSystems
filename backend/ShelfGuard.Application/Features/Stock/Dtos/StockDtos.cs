namespace ShelfGuard.Application.Features.Stock.Dtos;

public sealed record ProductStockDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductBarcode,
    Guid StoreId,
    string StoreName,
    Guid? ZoneId,
    string? ZoneName,
    int? ShelfNumber,
    string? BatchNumber,
    decimal Quantity,
    decimal QuantityInitial,
    DateOnly ExpiryDate,
    int DaysLeft,
    string Status,
    string? SourceType,
    DateTime AddedAt,
    DateTime LastCheckedAt
);

public sealed record CreateStockRequest(
    Guid ProductId,
    Guid StoreId,
    Guid? ZoneId,
    int? ShelfNumber,
    string? BatchNumber,
    decimal Quantity,
    DateOnly ExpiryDate,
    string? SourceType,
    Guid? SourceId
);

public sealed record UpdateStockRequest(
    Guid? ZoneId,
    int? ShelfNumber,
    string? BatchNumber,
    decimal Quantity,
    DateOnly ExpiryDate
);

public sealed record FefoConsumeRequest(
    Guid ProductId,
    Guid StoreId,
    decimal Quantity,
    string? Notes
);

public sealed record FefoConsumeResult(
    bool Success,
    decimal QuantityConsumed,
    decimal QuantityShortfall,
    List<BatchConsumed> BatchesConsumed,
    string? Error
);

public sealed record BatchConsumed(
    Guid StockId,
    string? BatchNumber,
    DateOnly ExpiryDate,
    decimal QuantityTaken
);

public sealed record SuggestionDto(
    Guid StockId,
    Guid ProductId,
    string ProductName,
    Guid StoreId,
    string StoreName,
    string? BatchNumber,
    DateOnly ExpiryDate,
    int DaysLeft,
    decimal Quantity,
    string Status,
    List<StockAction> Actions
);

public sealed record StockAction(
    string Type,
    string Label,
    string? TargetStoreName,
    Guid? TargetStoreId
);

public sealed record StockSummaryDto(
    int Safe,
    int Warning,
    int Critical,
    int Expired,
    int NeedsVerification,
    int Total
);
