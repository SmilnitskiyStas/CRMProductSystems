using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>EF Core implementation of <see cref="IProductionInsightsRepository"/> (read-only, RLS-scoped).</summary>
public sealed class ProductionInsightsRepository : IProductionInsightsRepository
{
    private readonly AppDbContext _db;

    public ProductionInsightsRepository(AppDbContext db) => _db = db;

    public Task<List<Location>> GetProductionLocationsAsync(Guid? locationId, CancellationToken ct = default)
    {
        var q = _db.Locations.AsNoTracking().Where(l => l.Type == "production" && l.IsActive);
        if (locationId.HasValue) q = q.Where(l => l.Id == locationId.Value);
        return q.OrderBy(l => l.Name).ToListAsync(ct);
    }

    public async Task<List<ProductionRequest>> GetRequestsAsync(
        IReadOnlyCollection<Guid> productionLocationIds, IReadOnlyCollection<string> statuses,
        DateTime? shippedFrom = null, DateTime? shippedToExclusive = null, CancellationToken ct = default)
    {
        var q = _db.ProductionRequests.AsNoTracking()
            .Include(r => r.StoreLocation)
            .Include(r => r.ShipmentTransfer)
            .Include(r => r.Lines).ThenInclude(l => l.Item)
            .Where(r => productionLocationIds.Contains(r.ProductionLocationId) && statuses.Contains(r.Status));
        if (shippedFrom.HasValue) q = q.Where(r => r.ShippedAt >= shippedFrom.Value);
        if (shippedToExclusive.HasValue) q = q.Where(r => r.ShippedAt < shippedToExclusive.Value);
        return await q.ToListAsync(ct);
    }

    public Task<List<ProductionRequest>> GetPipelineRequestsAsync(
        IReadOnlyCollection<Guid> productionLocationIds, DateTime fromUtc, DateTime toUtcExclusive,
        CancellationToken ct = default) =>
        _db.ProductionRequests.AsNoTracking()
            .Include(r => r.StoreLocation)
            .Include(r => r.ShipmentTransfer)
            .Include(r => r.Lines).ThenInclude(l => l.Item)
            .Where(r => productionLocationIds.Contains(r.ProductionLocationId)
                && (r.Status == ProductionRequestStatus.Submitted
                    || r.Status == ProductionRequestStatus.Accepted
                    || r.Status == ProductionRequestStatus.Partial
                    || (r.SubmittedAt >= fromUtc && r.SubmittedAt < toUtcExclusive)
                    || (r.ReviewedAt >= fromUtc && r.ReviewedAt < toUtcExclusive)
                    || (r.ShippedAt >= fromUtc && r.ShippedAt < toUtcExclusive)))
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<List<ProductionRequest>> GetAnalyticsRequestsAsync(
        IReadOnlyCollection<Guid> productionLocationIds, DateTime fromUtc, DateTime toUtcExclusive,
        CancellationToken ct = default) =>
        _db.ProductionRequests.AsNoTracking()
            .Include(r => r.StoreLocation)
            .Include(r => r.Lines).ThenInclude(l => l.Item)
            .Where(r => productionLocationIds.Contains(r.ProductionLocationId)
                && ((r.SubmittedAt >= fromUtc && r.SubmittedAt < toUtcExclusive)
                    || (r.ReviewedAt >= fromUtc && r.ReviewedAt < toUtcExclusive)
                    || (r.ShippedAt >= fromUtc && r.ShippedAt < toUtcExclusive)))
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<List<ProductionOrder>> GetCompletedOrdersAsync(
        IReadOnlyCollection<Guid> locationIds, DateTime fromUtc, DateTime toUtcExclusive,
        CancellationToken ct = default)
    {
        var a = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        var b = new DateTimeOffset(DateTime.SpecifyKind(toUtcExclusive, DateTimeKind.Utc));
        return _db.ProductionOrders.AsNoTracking()
            .Where(o => locationIds.Contains(o.LocationId) && o.CompletedAt >= a && o.CompletedAt < b)
            .ToListAsync(ct);
    }

    public Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default) =>
        _db.ProductionOrderRequestLines.AsNoTracking()
            .Include(l => l.ProductionOrder)
            .Where(l => l.RequestLine != null && requestIds.Contains(l.RequestLine.RequestId))
            .ToListAsync(ct);

    public Task<List<ProductionOrder>> GetOrdersAsync(
        IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<ProductionOrderStatus> statuses,
        CancellationToken ct = default) =>
        _db.ProductionOrders.AsNoTracking()
            .Include(o => o.Recipe).ThenInclude(r => r!.OutputItem)
            .Include(o => o.Recipe).ThenInclude(r => r!.Ingredients).ThenInclude(i => i.Item)
            .Where(o => locationIds.Contains(o.LocationId) && statuses.Contains(o.Status))
            .ToListAsync(ct);

    public Task<List<Recipe>> GetActiveRecipesAsync(CancellationToken ct = default) =>
        _db.Recipes.AsNoTracking()
            .Include(r => r.OutputItem)
            .Include(r => r.Ingredients).ThenInclude(i => i.Item)
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);

    public Task<List<ProductStock>> GetStockAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default) =>
        _db.ProductStocks.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Store)
            .Where(s => locationIds.Contains(s.StoreId) && s.Quantity > 0)
            .ToListAsync(ct);

    public Task<List<Item>> GetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        _db.Items.AsNoTracking().Include(i => i.DefaultSupplier).Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
}
