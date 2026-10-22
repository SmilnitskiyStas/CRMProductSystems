namespace ShelfGuard.Application.Features.Production.Dtos;

// ── Recipes ────────────────────────────────────────────────────────────────────

public record RecipeListItemDto(
    Guid Id,
    string Name,
    string OutputItemName,
    decimal OutputQty,
    string Unit,
    int IngredientCount,
    bool IsActive);

public record RecipeDetailDto(
    Guid Id,
    string Name,
    Guid OutputItemId,
    string OutputItemName,
    decimal OutputQty,
    string Unit,
    string? Notes,
    bool IsActive,
    List<RecipeIngredientDto> Ingredients);

public record RecipeIngredientDto(
    Guid Id,
    Guid ItemId,
    string ItemName,
    decimal Qty,
    string Unit);

public record RecipeCreateDto(
    string Name,
    Guid OutputItemId,
    decimal OutputQty,
    string Unit,
    string? Notes,
    List<RecipeIngredientCreateDto> Ingredients);

public record RecipeIngredientCreateDto(
    Guid ItemId,
    decimal Qty,
    string Unit);

public record RecipeUpdateDto(
    string? Name,
    string? Notes,
    bool? IsActive);

// ── Production Orders ──────────────────────────────────────────────────────────

public record ProductionOrderListItemDto(
    Guid Id,
    string RecipeName,
    string LocationName,
    decimal PlannedQty,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateOnly? NeededBy = null,
    DateOnly? PlannedDate = null);

public record ProductionOrderDetailDto(
    Guid Id,
    Guid RecipeId,
    string RecipeName,
    string LocationName,
    decimal PlannedQty,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    List<ProductionConsumptionDto> Consumptions,
    DateOnly? NeededBy = null,
    DateOnly? PlannedDate = null,
    List<ProductionOrderRequestRefDto>? Requests = null);

/// <summary>A store request allocation feeding a production order.</summary>
public record ProductionOrderRequestRefDto(
    Guid RequestId,
    Guid RequestLineId,
    string StoreLocationName,
    decimal Qty);

public record ProductionConsumptionDto(
    Guid ItemId,
    string ItemName,
    decimal QtyConsumed,
    string? BatchNumber,
    DateTimeOffset ConsumedAt);

public record ProductionOrderCreateDto(
    Guid RecipeId,
    Guid LocationId,
    decimal PlannedQty,
    string? Notes,
    DateOnly? NeededBy = null,
    DateOnly? PlannedDate = null);

/// <summary>
/// Status is accepted only for backward compatibility and must equal the current status:
/// transitions go through start / complete / cancel.
/// </summary>
public record ProductionOrderUpdateDto(
    string? Status,
    string? Notes,
    DateOnly? NeededBy = null,
    DateOnly? PlannedDate = null);

public record ProductionCompleteResultDto(
    Guid ProductionOrderId,
    Guid NewStockBatchId,
    decimal OutputQty,
    string OutputItemName);
