namespace ShelfGuard.Application.Features.Locations.Dtos;

public sealed record ShelfItemDto(
    Guid Id, Guid ShelfId, Guid ItemId, string ItemName, string? Barcode, int ExpectedFacings);

public sealed record ShelfSectionDto(Guid ShelfId, string Label, List<ShelfItemDto> Items);

public sealed record ShelfItemInput(Guid ItemId, int ExpectedFacings);

/// <summary>Replaces the whole planogram of one shelf section (items absent from the list are removed).</summary>
public sealed record SetShelfItemsRequest(Guid ShelfId, List<ShelfItemInput> Items);

/// <param name="Status">present | low | missing</param>
public sealed record ShelfScanItemDto(
    Guid ItemId, string Name, string Status, int EstimatedCount, double Confidence, string? Note);

/// <param name="Status">completed | failed</param>
public sealed record ShelfScanDto(
    Guid Id,
    Guid LocationId,
    Guid ZoneId,
    Guid? ShelfId,
    string ImageUrl,
    string Status,
    int MissingCount,
    int LowCount,
    bool AlertPending,
    string? Summary,
    List<ShelfScanItemDto> Items,
    string? Model,
    string? ErrorMessage,
    Guid? CreatedBy,
    DateTime CreatedAt);

/// <summary>Stored in <c>shelf_scans.ResultJson</c> (camelCase).</summary>
public sealed record ShelfScanResult(string? Summary, List<ShelfScanItemDto> Items);
