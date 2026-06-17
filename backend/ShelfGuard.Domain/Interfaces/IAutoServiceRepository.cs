using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository interface for the Auto Service module.
/// All queries are scoped to the calling tenant via RLS.
/// </summary>
public interface IAutoServiceRepository
{
    // ── Customers ────────────────────────────────────────────────────────────

    Task<List<AsCustomer>> GetCustomersAsync(string? search, CancellationToken ct = default);
    Task<AsCustomer?> GetCustomerByIdAsync(Guid id, CancellationToken ct = default);
    Task<int> GetVehicleCountForCustomerAsync(Guid customerId, CancellationToken ct = default);
    Task AddCustomerAsync(AsCustomer customer, CancellationToken ct = default);
    void UpdateCustomer(AsCustomer customer);
    void RemoveCustomer(AsCustomer customer);

    // ── Vehicles ─────────────────────────────────────────────────────────────

    Task<List<AsVehicle>> GetVehiclesAsync(Guid? customerId, CancellationToken ct = default);
    Task<AsVehicle?> GetVehicleByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasOpenWorkOrdersAsync(Guid vehicleId, CancellationToken ct = default);
    Task AddVehicleAsync(AsVehicle vehicle, CancellationToken ct = default);
    void UpdateVehicle(AsVehicle vehicle);
    void RemoveVehicle(AsVehicle vehicle);

    // ── Service Catalog ───────────────────────────────────────────────────────

    Task<List<AsServiceCatalog>> GetServiceCatalogAsync(bool includeInactive, CancellationToken ct = default);
    Task<AsServiceCatalog?> GetServiceCatalogItemByIdAsync(Guid id, CancellationToken ct = default);
    Task AddServiceCatalogItemAsync(AsServiceCatalog item, CancellationToken ct = default);
    void UpdateServiceCatalogItem(AsServiceCatalog item);

    // ── Work Orders ───────────────────────────────────────────────────────────

    Task<List<AsWorkOrder>> GetWorkOrdersAsync(
        string? status, Guid? vehicleId, Guid? mechanicUserId, CancellationToken ct = default);

    Task<AsWorkOrder?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct = default);
    Task AddWorkOrderAsync(AsWorkOrder workOrder, CancellationToken ct = default);
    void UpdateWorkOrder(AsWorkOrder workOrder);

    // ── Work Order Lines ──────────────────────────────────────────────────────

    Task AddWorkOrderLineAsync(AsWorkOrderLine line, CancellationToken ct = default);
    void RemoveWorkOrderLine(AsWorkOrderLine line);

    // ── Stock (FEFO write-down) ───────────────────────────────────────────────

    Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Returns batches for itemId ordered by expiry_date ASC (FEFO).</summary>
    Task<List<ProductStock>> GetFefoOrderedAsync(Guid itemId, CancellationToken ct = default);

    void UpdateStock(ProductStock batch);
    Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────────────────

    Task SaveChangesAsync(CancellationToken ct = default);
}
