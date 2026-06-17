using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// EF Core implementation of IAutoServiceRepository.
/// Tenant isolation is enforced by PostgreSQL RLS via TenantConnectionInterceptor.
/// </summary>
public sealed class AutoServiceRepository : IAutoServiceRepository
{
    private readonly AppDbContext _db;

    public AutoServiceRepository(AppDbContext db) => _db = db;

    // ── Customers ────────────────────────────────────────────────────────────

    public async Task<List<AsCustomer>> GetCustomersAsync(string? search, CancellationToken ct = default)
    {
        var query = _db.AsCustomers.Include(c => c.Vehicles).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(lower) ||
                (c.Phone != null && c.Phone.Contains(search)));
        }

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<AsCustomer?> GetCustomerByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AsCustomers
           .Include(c => c.Vehicles)
           .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<int> GetVehicleCountForCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        _db.AsVehicles.CountAsync(v => v.CustomerId == customerId, ct);

    public Task AddCustomerAsync(AsCustomer customer, CancellationToken ct = default) =>
        _db.AsCustomers.AddAsync(customer, ct).AsTask();

    public void UpdateCustomer(AsCustomer customer) =>
        _db.AsCustomers.Update(customer);

    public void RemoveCustomer(AsCustomer customer) =>
        _db.AsCustomers.Remove(customer);

    // ── Vehicles ─────────────────────────────────────────────────────────────

    public async Task<List<AsVehicle>> GetVehiclesAsync(Guid? customerId, CancellationToken ct = default)
    {
        var query = _db.AsVehicles
            .Include(v => v.Customer)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(v => v.CustomerId == customerId.Value);

        return await query.OrderBy(v => v.Brand).ThenBy(v => v.Model).ToListAsync(ct);
    }

    public Task<AsVehicle?> GetVehicleByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AsVehicles
           .Include(v => v.Customer)
           .Include(v => v.WorkOrders)
           .FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<bool> HasOpenWorkOrdersAsync(Guid vehicleId, CancellationToken ct = default) =>
        _db.AsWorkOrders.AnyAsync(w =>
            w.VehicleId == vehicleId &&
            w.Status != WorkOrderStatus.Done &&
            w.Status != WorkOrderStatus.Invoiced,
            ct);

    public Task AddVehicleAsync(AsVehicle vehicle, CancellationToken ct = default) =>
        _db.AsVehicles.AddAsync(vehicle, ct).AsTask();

    public void UpdateVehicle(AsVehicle vehicle) =>
        _db.AsVehicles.Update(vehicle);

    public void RemoveVehicle(AsVehicle vehicle) =>
        _db.AsVehicles.Remove(vehicle);

    // ── Service Catalog ───────────────────────────────────────────────────────

    public async Task<List<AsServiceCatalog>> GetServiceCatalogAsync(bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.AsServiceCatalogs.AsQueryable();
        if (!includeInactive)
            query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public Task<AsServiceCatalog?> GetServiceCatalogItemByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AsServiceCatalogs.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task AddServiceCatalogItemAsync(AsServiceCatalog item, CancellationToken ct = default) =>
        _db.AsServiceCatalogs.AddAsync(item, ct).AsTask();

    public void UpdateServiceCatalogItem(AsServiceCatalog item) =>
        _db.AsServiceCatalogs.Update(item);

    // ── Work Orders ───────────────────────────────────────────────────────────

    public async Task<List<AsWorkOrder>> GetWorkOrdersAsync(
        string? status, Guid? vehicleId, Guid? mechanicUserId, CancellationToken ct = default)
    {
        var query = _db.AsWorkOrders
            .Include(w => w.Vehicle)
                .ThenInclude(v => v!.Customer)
            .Include(w => w.Mechanic)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<WorkOrderStatus>(status, true, out var statusEnum))
        {
            query = query.Where(w => w.Status == statusEnum);
        }

        if (vehicleId.HasValue)
            query = query.Where(w => w.VehicleId == vehicleId.Value);

        if (mechanicUserId.HasValue)
            query = query.Where(w => w.MechanicUserId == mechanicUserId.Value);

        return await query.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
    }

    public Task<AsWorkOrder?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AsWorkOrders
           .Include(w => w.Vehicle)
               .ThenInclude(v => v!.Customer)
           .Include(w => w.Mechanic)
           .Include(w => w.Lines)
               .ThenInclude(l => l.ServiceCatalog)
           .Include(w => w.Lines)
               .ThenInclude(l => l.Item)
           .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task AddWorkOrderAsync(AsWorkOrder workOrder, CancellationToken ct = default) =>
        _db.AsWorkOrders.AddAsync(workOrder, ct).AsTask();

    public void UpdateWorkOrder(AsWorkOrder workOrder) =>
        _db.AsWorkOrders.Update(workOrder);

    // ── Work Order Lines ──────────────────────────────────────────────────────

    public Task AddWorkOrderLineAsync(AsWorkOrderLine line, CancellationToken ct = default) =>
        _db.AsWorkOrderLines.AddAsync(line, ct).AsTask();

    public void RemoveWorkOrderLine(AsWorkOrderLine line) =>
        _db.AsWorkOrderLines.Remove(line);

    // ── Stock (FEFO write-down) ───────────────────────────────────────────────

    public Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default) =>
        _db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);

    public async Task<List<ProductStock>> GetFefoOrderedAsync(Guid itemId, CancellationToken ct = default) =>
        await _db.ProductStocks
            .Where(s => s.ProductId == itemId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public void UpdateStock(ProductStock batch) =>
        _db.ProductStocks.Update(batch);

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) =>
        _db.StockEvents.AddAsync(ev, ct).AsTask();

    // ─────────────────────────────────────────────────────────────────────────

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
