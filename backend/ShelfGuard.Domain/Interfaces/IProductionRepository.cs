using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository interface for the Production module.
/// All queries are scoped to the calling tenant via RLS.
/// </summary>
public interface IProductionRepository
{
    // ── Recipes ───────────────────────────────────────────────────────────────

    Task<List<Recipe>> GetRecipesAsync(bool includeInactive, CancellationToken ct = default);
    Task<Recipe?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasActiveOrdersForRecipeAsync(Guid recipeId, CancellationToken ct = default);
    Task AddRecipeAsync(Recipe recipe, CancellationToken ct = default);
    void UpdateRecipe(Recipe recipe);

    // ── Recipe Ingredients ────────────────────────────────────────────────────

    void RemoveIngredients(IEnumerable<RecipeIngredient> ingredients);
    Task AddIngredientAsync(RecipeIngredient ingredient, CancellationToken ct = default);

    // ── Production Orders ─────────────────────────────────────────────────────

    Task<List<ProductionOrder>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default);

    Task<ProductionOrder?> GetOrderByIdAsync(Guid id, CancellationToken ct = default);
    Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default);
    void UpdateOrder(ProductionOrder order);

    // ── Consumptions ──────────────────────────────────────────────────────────

    Task AddConsumptionAsync(ProductionOrderConsumption consumption, CancellationToken ct = default);

    // ── Stock / Items (FEFO write-down) ───────────────────────────────────────

    Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Returns batches for itemId ordered by expiry_date ASC (FEFO).</summary>
    Task<List<ProductStock>> GetFefoOrderedAsync(Guid itemId, Guid locationId, CancellationToken ct = default);

    void UpdateStock(ProductStock batch);
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);
    Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────────────────

    Task SaveChangesAsync(CancellationToken ct = default);
}
