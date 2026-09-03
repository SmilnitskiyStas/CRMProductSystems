using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
/// Parallel to <see cref="StockRepository"/>. RLS scopes every query to the caller's tenant;
/// the explicit <c>TenantId</c> predicates are defence-in-depth on top of that.
/// No <c>AsNoTracking</c> on the single-row / FEFO reads — the service mutates the loaded
/// entities before <c>SaveChangesAsync</c> (same as <see cref="StockRepository"/>).
/// </summary>
public sealed class SupplierStockRepository : ISupplierStockRepository
{
    private readonly AppDbContext _db;

    public SupplierStockRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<SupplierStock> Items, int Total)> GetPagedAsync(
        Guid tenantId, Guid? warehouseId, Guid? supplierItemId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SupplierStocks
            .Include(s => s.SupplierItem).ThenInclude(i => i!.Item)
            .Include(s => s.Warehouse)
            .Where(s => s.TenantId == tenantId);

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        if (supplierItemId.HasValue)
            query = query.Where(s => s.SupplierItemId == supplierItemId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.ExpiryDate)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<List<SupplierStock>> GetFefoOrderedAsync(
        Guid tenantId, Guid supplierItemId, Guid warehouseId, CancellationToken ct = default) =>
        _db.SupplierStocks
            .Where(s => s.TenantId == tenantId
                     && s.SupplierItemId == supplierItemId
                     && s.WarehouseId == warehouseId
                     && s.Quantity > 0
                     && s.Status != "sold_out" && s.Status != "archived")
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public Task<SupplierStock?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.SupplierStocks
            .Include(s => s.SupplierItem).ThenInclude(i => i!.Item)
            .Include(s => s.Warehouse)
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public Task<bool> WarehouseExistsAsync(Guid tenantId, Guid warehouseId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(
            l => l.Id == warehouseId && l.TenantId == tenantId && l.IsActive && l.Type == "warehouse", ct);

    public Task<bool> SupplierItemExistsAsync(Guid tenantId, Guid supplierItemId, CancellationToken ct = default) =>
        _db.SupplierItems.AnyAsync(i => i.Id == supplierItemId && i.TenantId == tenantId, ct);

    public async Task AddAsync(SupplierStock stock, CancellationToken ct = default) =>
        await _db.SupplierStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(SupplierStockMovement movement, CancellationToken ct = default) =>
        await _db.SupplierStockMovements.AddAsync(movement, ct);

    public void Update(SupplierStock stock) => _db.SupplierStocks.Update(stock);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // SupplierStock.Quantity carries an xmin token (see AppDbContext) — a concurrent
            // adjust or a Phase 3 shipment touched the same batch. Translate to a Domain
            // exception so Application services (which must not reference EF Core) can catch it.
            throw new ConcurrencyConflictException(
                "One or more supplier stock rows were modified concurrently by another operation.", ex);
        }
    }
}
