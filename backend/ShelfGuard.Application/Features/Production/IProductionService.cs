using ShelfGuard.Application.Features.Production.Dtos;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Application service interface for the Production module.
/// All operations are tenant-scoped (RLS enforced at DB layer).
/// </summary>
public interface IProductionService
{
    // ── Recipes ───────────────────────────────────────────────────────────────

    Task<List<RecipeListItemDto>> GetRecipesAsync(bool includeInactive, CancellationToken ct = default);

    Task<RecipeDetailDto?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default);

    Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> CreateRecipeAsync(
        RecipeCreateDto dto, Guid tenantId, CancellationToken ct = default);

    Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> UpdateRecipeAsync(
        Guid id, RecipeUpdateDto dto, CancellationToken ct = default);

    Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> ReplaceIngredientsAsync(
        Guid recipeId, List<RecipeIngredientCreateDto> ingredients, CancellationToken ct = default);

    /// <summary>Soft-delete: sets IsActive = false. Returns 409 if recipe has active orders.</summary>
    Task<(bool Ok, string? Error, int? StatusCode)> DeactivateRecipeAsync(
        Guid id, CancellationToken ct = default);

    // ── Production Orders ─────────────────────────────────────────────────────

    Task<List<ProductionOrderListItemDto>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default);

    Task<ProductionOrderDetailDto?> GetOrderByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> CreateOrderAsync(
        ProductionOrderCreateDto dto, Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> UpdateOrderAsync(
        Guid id, ProductionOrderUpdateDto dto, CancellationToken ct = default);

    /// <summary>Transitions order to InProgress (must be Planned).</summary>
    Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> StartOrderAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>
    /// Completes an order: FEFO write-down of ingredients + creates finished product batch.
    /// Atomic — rolls back on insufficient stock (422).
    /// Allowed from Planned or InProgress status (409 otherwise).
    /// Returns InsufficientItemId when stock is insufficient (422).
    /// Returns OutputStockBatchId of the newly created stock row on success.
    /// </summary>
    Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode, Guid? InsufficientItemId, Guid? OutputStockBatchId)> CompleteOrderAsync(
        Guid id, Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Cancels order. Returns 409 if already Done.</summary>
    Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> CancelOrderAsync(
        Guid id, CancellationToken ct = default);
}
