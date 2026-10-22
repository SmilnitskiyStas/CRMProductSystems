namespace ShelfGuard.Application.Features.Production.Dtos;

// ── Store -> production requests ──────────────────────────────────────────────

public record ProductionRequestListItemDto(
    Guid Id,
    Guid StoreLocationId,
    string StoreLocationName,
    Guid ProductionLocationId,
    string ProductionLocationName,
    DateOnly NeededBy,
    string Status,
    string Source,
    int LineCount,
    decimal TotalRequestedQty,
    decimal? TotalAcceptedQty,
    DateTime CreatedAt,
    DateTime? SubmittedAt);

public record ProductionRequestLineDto(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Unit,
    decimal RequestedQty,
    decimal? AcceptedQty,
    string? Reason);

public record ProductionRequestDetailDto(
    Guid Id,
    Guid StoreLocationId,
    string StoreLocationName,
    Guid ProductionLocationId,
    string ProductionLocationName,
    DateOnly NeededBy,
    string Status,
    string Source,
    string? Notes,
    string? ReviewNote,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ShippedAt,
    Guid? ShipmentTransferId,
    string? ShipmentStatus,
    List<ProductionRequestLineDto> Lines);

public record ProductionRequestLineCreateDto(Guid ItemId, decimal RequestedQty);

/// <summary>Create a request (draft). Set <c>Submit = true</c> to send it to production immediately.</summary>
public record ProductionRequestCreateDto(
    Guid StoreLocationId,
    Guid ProductionLocationId,
    DateOnly NeededBy,
    string? Notes,
    List<ProductionRequestLineCreateDto> Lines,
    bool Submit = false);

/// <summary>Draft-only edit. Null fields are left unchanged; a non-null <c>Lines</c> replaces all lines.</summary>
public record ProductionRequestUpdateDto(
    DateOnly? NeededBy,
    string? Notes,
    List<ProductionRequestLineCreateDto>? Lines);

public record ProductionRequestLineReviewDto(Guid LineId, decimal AcceptedQty, string? Reason);

/// <summary>
/// Production review of a submitted request. Lines not listed are accepted in full. Reason is
/// required for every line accepted below the requested quantity.
/// </summary>
public record ProductionRequestReviewDto(
    List<ProductionRequestLineReviewDto> Lines,
    string? Note);

public record ProductionRequestRejectDto(string Reason);

/// <summary>Per-item aggregate of open requests for the production cabinet.</summary>
public record ProductionRequestSummaryItemDto(
    Guid ItemId,
    string ItemName,
    string Unit,
    int RequestCount,
    decimal RequestedQty,
    /// <summary>Qty still to produce: accepted (or requested while unreviewed) minus lines already in an active order.</summary>
    decimal QtyToProduce,
    bool HasRecipe,
    DateOnly EarliestNeededBy);

/// <summary>Create production orders (one per item, aggregated) from accepted/partial requests.</summary>
public record ProductionOrdersFromRequestsDto(
    List<Guid> RequestIds,
    DateOnly? PlannedDate,
    string? Notes);

public record ProductionOrderCreatedDto(
    Guid OrderId,
    Guid ItemId,
    string ItemName,
    string RecipeName,
    decimal PlannedQty,
    int RequestLineCount);
