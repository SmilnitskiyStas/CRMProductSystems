using ShelfGuard.Application.Features.SupplierInventory.Dtos;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 1 (plan `1-partitioned-book.md`, decision D1).
/// Thin wrapper over <see cref="Locations.ILocationService"/> that presents a supplier
/// tenant's warehouse locations (Location.Type = "warehouse") through the supplier
/// cabinet, without the zones / floor-plan / store-scope surface the retail
/// LocationsController exposes. Every operation is scoped to the calling supplier
/// tenant — no location id from another tenant is ever accepted.
/// Gated by the "supplier_inventory" module at the controller.
/// </summary>
public interface ISupplierWarehouseService
{
    Task<List<SupplierWarehouseDto>> ListAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SupplierWarehouseDto? Warehouse, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierWarehouseRequest request, CancellationToken ct = default);

    Task<(SupplierWarehouseDto? Warehouse, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateSupplierWarehouseRequest request, CancellationToken ct = default);

    Task<(bool Success, string? Error)> DeactivateAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);
}
