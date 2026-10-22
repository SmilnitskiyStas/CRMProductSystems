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
            .OrderBy(r => r.CreatedAt)
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

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
