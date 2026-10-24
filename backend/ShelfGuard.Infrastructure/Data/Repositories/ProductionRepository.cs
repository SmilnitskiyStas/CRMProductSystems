using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// EF Core implementation of IProductionRepository.
/// Tenant isolation is enforced by PostgreSQL RLS via TenantConnectionInterceptor.
/// </summary>
public sealed class ProductionRepository : IProductionRepository
{
    private readonly AppDbContext _db;
    private readonly IWorkAccessContext _access;

    /// <summary>Ukrainian-collation ordering only where the provider supports it (Npgsql).</summary>
    private bool Uk => _db.Database.IsNpgsql();

    public ProductionRepository(AppDbContext db, IWorkAccessContext access) { _db = db; _access = access; }

    // ── Recipes ───────────────────────────────────────────────────────────────

    public async Task<List<Recipe>> GetRecipesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null) return [];
        var query = _db.Recipes
            .Where(r => r.TenantId == scope.TenantId)
            .Include(r => r.OutputItem)
            .Include(r => r.Ingredients)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        return await query.OrderByUk(r => r.Name, Uk).ToListAsync(ct);
    }

    public async Task<Recipe?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null) return null;
        return await _db.Recipes.Where(r => r.TenantId == scope.TenantId)
           .Include(r => r.OutputItem)
           .Include(r => r.Ingredients)
               .ThenInclude(i => i.Item)
           .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> HasActiveOrdersForRecipeAsync(Guid recipeId, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        // A recipe cannot change while any order in its tenant depends on it.
        return await _db.ProductionOrders.AnyAsync(o => o.TenantId == scope.TenantId && o.RecipeId == recipeId
            && (o.Status == ProductionOrderStatus.Planned || o.Status == ProductionOrderStatus.InProgress), ct);
    }

    public Task AddRecipeAsync(Recipe recipe, CancellationToken ct = default) =>
        _db.Recipes.AddAsync(recipe, ct).AsTask();

    public void UpdateRecipe(Recipe recipe) =>
        _db.Entry(recipe).State = EntityState.Modified;

    // ── Recipe Ingredients ────────────────────────────────────────────────────

    public void RemoveIngredients(IEnumerable<RecipeIngredient> ingredients) =>
        _db.RecipeIngredients.RemoveRange(ingredients);

    public Task AddIngredientAsync(RecipeIngredient ingredient, CancellationToken ct = default) =>
        _db.RecipeIngredients.AddAsync(ingredient, ct).AsTask();

    // ── Production Orders ─────────────────────────────────────────────────────

    public async Task<List<ProductionOrder>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null) return [];
        var query = _db.ProductionOrders
            .Where(o => o.TenantId == scope.TenantId && (scope.TenantWide || scope.LocationIds.Contains(o.LocationId)))
            .Include(o => o.Recipe)
            .Include(o => o.Location)
            .Include(o => o.RequestLines).ThenInclude(l => l.RequestLine!).ThenInclude(rl => rl.Request)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            ProductionOrderStatusExtensions.TryParseApi(status, out var statusEnum))
        {
            query = query.Where(o => o.Status == statusEnum);
        }

        if (recipeId.HasValue)
            query = query.Where(o => o.RecipeId == recipeId.Value);

        if (locationId.HasValue)
            query = query.Where(o => o.LocationId == locationId.Value);

        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public async Task<ProductionOrder?> GetOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null) return null;
        return await _db.ProductionOrders.Where(o => o.TenantId == scope.TenantId && (scope.TenantWide || scope.LocationIds.Contains(o.LocationId)))
           .Include(o => o.Recipe)
               .ThenInclude(r => r!.Ingredients)
                   .ThenInclude(i => i.Item)
           .Include(o => o.Location)
           .Include(o => o.Consumptions)
               .ThenInclude(c => c.Item)
           .Include(o => o.RequestLines)
               .ThenInclude(l => l.RequestLine!)
                   .ThenInclude(rl => rl.Request!)
                       .ThenInclude(r => r.StoreLocation)
           .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default) =>
        _db.ProductionOrders.AddAsync(order, ct).AsTask();

    public void UpdateOrder(ProductionOrder order) =>
        _db.Entry(order).State = EntityState.Modified;

    // ── Consumptions ──────────────────────────────────────────────────────────

    public Task AddConsumptionAsync(ProductionOrderConsumption consumption, CancellationToken ct = default) =>
        _db.ProductionOrderConsumptions.AddAsync(consumption, ct).AsTask();

    // ── Stock / Items (FEFO write-down) ───────────────────────────────────────

    public async Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        return scope == null ? null : await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.TenantId == scope.TenantId, ct);
    }

    public Task<Location?> GetLocationByIdAsync(Guid locationId, CancellationToken ct = default) =>
        _db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, ct);

    public async Task<List<ProductStock>> GetFefoOrderedAsync(
        Guid itemId, Guid locationId, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null || !scope.CanLocation(locationId)) return [];
        return await _db.ProductStocks
            .Where(s => s.TenantId == scope.TenantId && s.ProductId == itemId && s.StoreId == locationId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);
    }

    public void UpdateStock(ProductStock batch) =>
        _db.Entry(batch).State = EntityState.Modified;

    public Task AddStockAsync(ProductStock stock, CancellationToken ct = default) =>
        _db.ProductStocks.AddAsync(stock, ct).AsTask();

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) =>
        _db.StockEvents.AddAsync(ev, ct).AsTask();

    // ─────────────────────────────────────────────────────────────────────────

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        _db.ChangeTracker.DetectChanges();
        foreach (var entry in _db.ChangeTracker.Entries<Recipe>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            if (entry.Entity.TenantId != scope.TenantId || !scope.Has("production.manage")
                || !await _db.Items.AnyAsync(i => i.Id == entry.Entity.OutputItemId && i.TenantId == scope.TenantId, ct)) throw new WorkAccessDeniedException();
        foreach (var entry in _db.ChangeTracker.Entries<RecipeIngredient>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var parent = _db.ChangeTracker.Entries<Recipe>().Any(e => e.Entity.Id == entry.Entity.RecipeId && e.Entity.TenantId == scope.TenantId)
                || await _db.Recipes.AnyAsync(r => r.Id == entry.Entity.RecipeId && r.TenantId == scope.TenantId, ct);
            if (!parent || !scope.Has("production.manage") || !await _db.Items.AnyAsync(i => i.Id == entry.Entity.ItemId && i.TenantId == scope.TenantId, ct)) throw new WorkAccessDeniedException();
        }
        foreach (var entry in _db.ChangeTracker.Entries<ProductionOrder>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var order = entry.Entity;
            if (order.TenantId != scope.TenantId || !scope.CanLocation(order.LocationId)
                || !await _db.Locations.AnyAsync(l => l.Id == order.LocationId && l.TenantId == scope.TenantId, ct)
                || !await _db.Recipes.AnyAsync(r => r.Id == order.RecipeId && r.TenantId == scope.TenantId, ct)) throw new WorkAccessDeniedException();
            if (entry.State == EntityState.Added)
            { if (!scope.Has("production.manage") || order.Status != ProductionOrderStatus.Planned) throw new WorkAccessDeniedException(); continue; }
            if (entry.Property(o => o.RecipeId).OriginalValue != order.RecipeId || entry.Property(o => o.LocationId).OriginalValue != order.LocationId
                || entry.Property(o => o.TenantId).OriginalValue != order.TenantId) throw new WorkAccessDeniedException();
            var previous = entry.Property(o => o.Status).OriginalValue;
            var allowed = order.Status == previous ? previous == ProductionOrderStatus.Planned && scope.Has("production.manage")
                : (previous == ProductionOrderStatus.Planned && order.Status == ProductionOrderStatus.InProgress
                    || previous == ProductionOrderStatus.InProgress && order.Status == ProductionOrderStatus.Done) && scope.Has("production.complete")
                    || (previous is ProductionOrderStatus.Planned or ProductionOrderStatus.InProgress) && order.Status == ProductionOrderStatus.Cancelled && scope.Has("production.cancel");
            if (!allowed) throw new WorkAccessDeniedException();
        }
        foreach (var entry in _db.ChangeTracker.Entries<ProductStock>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            if (!scope.Has("production.complete") || entry.Entity.TenantId != scope.TenantId || !scope.CanLocation(entry.Entity.StoreId) || entry.Entity.Quantity < 0)
                throw new WorkAccessDeniedException();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ProductStock?> GetOutputForOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var scope = await _access.GetAsync(ct);
        return scope == null ? null : await _db.ProductStocks.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == scope.TenantId
            && s.SourceType == "production" && s.SourceId == orderId && (scope.TenantWide || scope.LocationIds.Contains(s.StoreId)), ct);
    }
}
