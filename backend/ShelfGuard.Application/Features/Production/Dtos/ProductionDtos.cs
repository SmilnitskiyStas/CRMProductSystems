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
    DateTimeOffset? CompletedAt);

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
    List<ProductionConsumptionDto> Consumptions);

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
    string? Notes);

public record ProductionOrderUpdateDto(
    string? Status,
    string? Notes);

public record ProductionCompleteResultDto(
    Guid ProductionOrderId,
    Guid NewStockBatchId,
    decimal OutputQty,
    string OutputItemName);
