namespace ShelfGuard.Application.Features.Sales.Dtos;

public sealed record DailySaleDto(
    Guid Id,
    Guid StoreId,
    string StoreName,
    Guid ProductId,
    string ProductName,
    string? Barcode,
    DateOnly Date,
    decimal QuantitySold,
    decimal? QuantityEndOfDay,
    bool IsPromoDay,
    bool IsAnomaly,
    string Source
);

public sealed record UpsertDailySaleRequest(
    Guid StoreId,
    Guid ProductId,
    DateOnly Date,
    decimal QuantitySold,
    decimal? QuantityEndOfDay,
    bool IsPromoDay
);

public sealed record MarkAnomalyRequest(bool IsAnomaly);

public sealed record CsvImportResult(
    int Created,
    int Updated,
    int Skipped,
    List<string> Errors
);
