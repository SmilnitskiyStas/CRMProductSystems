using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>EF Core implementation of <see cref="IProductionRequestRepository"/>. Isolation via RLS.</summary>
public sealed class ProductionRequestRepository : IProductionRequestRepository
{
    private readonly AppDbContext _db;

    public ProductionRequestRepository(AppDbContext db) => _db = db;

    private IQueryable<ProductionRequest> WithGraph() =>
        _db.ProductionRequests
            .Include(r => r.StoreLocation)
            .Include(r => r.ProductionLocation)
            .Include(r => r.ShipmentTransfer)
            .Include(r => r.Lines).ThenInclude(l => l.Item);

    public Task<ProductionRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        WithGraph().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<ProductionRequest>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        WithGraph().Where(r => ids.Contains(r.Id)).ToListAsync(ct);

    public async Task<List<ProductionRequest>> ListAsync(
        Guid? storeLocationId, Guid? productionLocationId, IReadOnlyCollection<string>? statuses,
        DateOnly? neededFrom, DateOnly? neededTo, CancellationToken ct = default)
    {
        var q = WithGraph();
        if (storeLocationId.HasValue) q = q.Where(r => r.StoreLocationId == storeLocationId.Value);
        if (productionLocationId.HasValue) q = q.Where(r => r.ProductionLocationId == productionLocationId.Value);
        if (statuses is { Count: > 0 }) q = q.Where(r => statuses.Contains(r.Status));
        if (neededFrom.HasValue) q = q.Where(r => r.NeededBy >= neededFrom.Value);
        if (neededTo.HasValue) q = q.Where(r => r.NeededBy <= neededTo.Value);
        return await q.OrderBy(r => r.NeededBy).ThenByDescending(r => r.CreatedAt).ToListAsync(ct);
    }

    public Task AddAsync(ProductionRequest request, CancellationToken ct = default) =>
        _db.ProductionRequests.AddAsync(request, ct).AsTask();

    // Explicit DbSet.Add: a client-set Guid added via a tracked parent's navigation can be misread as Modified.
    public Task AddLinesAsync(IEnumerable<ProductionRequestLine> lines, CancellationToken ct = default) =>
        _db.ProductionRequestLines.AddRangeAsync(lines, ct);

    public void RemoveLines(IEnumerable<ProductionRequestLine> lines) =>
        _db.ProductionRequestLines.RemoveRange(lines);

    public Task<Location?> GetLocationAsync(Guid id, CancellationToken ct = default) =>
        _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<Recipe?> GetActiveRecipeForOutputAsync(Guid itemId, CancellationToken ct = default) =>
        _db.Recipes.Where(r => r.IsActive && r.OutputItemId == itemId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<HashSet<Guid>> GetItemIdsWithActiveRecipeAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        (await _db.Recipes.Where(r => r.IsActive && itemIds.Contains(r.OutputItemId))
            .Select(r => r.OutputItemId).Distinct().ToListAsync(ct)).ToHashSet();

    public async Task<HashSet<Guid>> GetLineIdsInActiveOrdersAsync(
        IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default) =>
        (await _db.ProductionOrderRequestLines
            .Where(l => lineIds.Contains(l.RequestLineId)
                        && l.ProductionOrder!.Status != ProductionOrderStatus.Cancelled)
            .Select(l => l.RequestLineId).Distinct().ToListAsync(ct)).ToHashSet();

    public Task AddOrderLinksAsync(IEnumerable<ProductionOrderRequestLine> links, CancellationToken ct = default) =>
        _db.ProductionOrderRequestLines.AddRangeAsync(links, ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        // Join an ambient transaction (e.g. a caller that already opened one) instead of nesting.
        if (_db.Database.CurrentTransaction is not null || !_db.Database.IsRelational())
            return await work();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task LockOrderKeysAsync(IEnumerable<(Guid LocationId, Guid ItemId)> keys, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        foreach (var (loc, item) in keys.Distinct().OrderBy(k => k.LocationId).ThenBy(k => k.ItemId))
        {
            var key = $"prod-order:{loc:N}:{item:N}";
            // Blocks until the holder commits/rolls back; released automatically at transaction end.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
    }

    public Task<List<ProductionOrderRequestLine>> GetLinksForLinesAsync(
        IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default) =>
        _db.ProductionOrderRequestLines
            .Include(l => l.ProductionOrder)
            .Where(l => lineIds.Contains(l.RequestLineId))
            .ToListAsync(ct);

    public Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default) =>
        _db.ProductionOrderRequestLines
            .Include(l => l.ProductionOrder)
            .Include(l => l.RequestLine)
            .Where(l => requestIds.Contains(l.RequestLine!.RequestId))
            .ToListAsync(ct);

    public Task<List<ProductionOrderRequestLine>> GetLinksForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        _db.ProductionOrderRequestLines
            .Include(l => l.RequestLine)
            .Where(l => l.ProductionOrderId == orderId)
            .ToListAsync(ct);

    public Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default) =>
        _db.ProductionOrders.AddAsync(order, ct).AsTask();

    public void RemoveOrderLinks(IEnumerable<ProductionOrderRequestLine> links) =>
        _db.ProductionOrderRequestLines.RemoveRange(links);

    public Task<ProductionOrder?> FindPlannedOrderAsync(
        Guid locationId, Guid itemId, DateOnly neededBy, CancellationToken ct = default) =>
        _db.ProductionOrders
            .Where(o => o.Status == ProductionOrderStatus.Planned
                        && o.LocationId == locationId
                        && o.NeededBy == neededBy
                        && o.Recipe!.OutputItemId == itemId)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
