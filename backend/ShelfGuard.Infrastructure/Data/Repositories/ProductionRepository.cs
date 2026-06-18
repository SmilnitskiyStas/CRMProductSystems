using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// EF Core implementation of IProductionRepository.
/// Tenant isolation is enforced by PostgreSQL RLS via TenantConnectionInterceptor.
/// </summary>
public sealed class ProductionRepository : IProductionRepository
{
    private readonly AppDbContext _db;

    public ProductionRepository(AppDbContext db) => _db = db;

    // ── Recipes ───────────────────────────────────────────────────────────────

    public async Task<List<Recipe>> GetRecipesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.Recipes
            .Include(r => r.OutputItem)
            .Include(r => r.Ingredients)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        return await query.OrderBy(r => r.Name).ToListAsync(ct);
    }

    public Task<Recipe?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Recipes
           .Include(r => r.OutputItem)
           .Include(r => r.Ingredients)
               .ThenInclude(i => i.Item)
           .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> HasActiveOrdersForRecipeAsync(Guid recipeId, CancellationToken ct = default) =>
        _db.ProductionOrders.AnyAsync(o =>
            o.RecipeId == recipeId &&
            (o.Status == ProductionOrderStatus.Planned || o.Status == ProductionOrderStatus.InProgress),
            ct);

    public Task AddRecipeAsync(Recipe recipe, CancellationToken ct = default) =>
        _db.Recipes.AddAsync(recipe, ct).AsTask();

    public void UpdateRecipe(Recipe recipe) =>
        _db.Recipes.Update(recipe);

    // ── Recipe Ingredients ────────────────────────────────────────────────────

    public void RemoveIngredients(IEnumerable<RecipeIngredient> ingredients) =>
        _db.RecipeIngredients.RemoveRange(ingredients);

    public Task AddIngredientAsync(RecipeIngredient ingredient, CancellationToken ct = default) =>
        _db.RecipeIngredients.AddAsync(ingredient, ct).AsTask();

    // ── Production Orders ─────────────────────────────────────────────────────

    public async Task<List<ProductionOrder>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default)
    {
        var query = _db.ProductionOrders
            .Include(o => o.Recipe)
            .Include(o => o.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ProductionOrderStatus>(status, true, out var statusEnum))
        {
            query = query.Where(o => o.Status == statusEnum);
        }

        if (recipeId.HasValue)
            query = query.Where(o => o.RecipeId == recipeId.Value);

        if (locationId.HasValue)
            query = query.Where(o => o.LocationId == locationId.Value);

        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public Task<ProductionOrder?> GetOrderByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ProductionOrders
           .Include(o => o.Recipe)
               .ThenInclude(r => r!.Ingredients)
                   .ThenInclude(i => i.Item)
           .Include(o => o.Location)
           .Include(o => o.Consumptions)
               .ThenInclude(c => c.Item)
           .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default) =>
        _db.ProductionOrders.AddAsync(order, ct).AsTask();

    public void UpdateOrder(ProductionOrder order) =>
        _db.ProductionOrders.Update(order);

    // ── Consumptions ──────────────────────────────────────────────────────────

    public Task AddConsumptionAsync(ProductionOrderConsumption consumption, CancellationToken ct = default) =>
        _db.ProductionOrderConsumptions.AddAsync(consumption, ct).AsTask();

    // ── Stock / Items (FEFO write-down) ───────────────────────────────────────

    public Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default) =>
        _db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);

    public async Task<List<ProductStock>> GetFefoOrderedAsync(
        Guid itemId, Guid locationId, CancellationToken ct = default) =>
        await _db.ProductStocks
            .Where(s => s.ProductId == itemId && s.StoreId == locationId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public void UpdateStock(ProductStock batch) =>
        _db.ProductStocks.Update(batch);

    public Task AddStockAsync(ProductStock stock, CancellationToken ct = default) =>
        _db.ProductStocks.AddAsync(stock, ct).AsTask();

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) =>
        _db.StockEvents.AddAsync(ev, ct).AsTask();

    // ─────────────────────────────────────────────────────────────────────────

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
