using ShelfGuard.Application.Features.AutoService.Dtos;

namespace ShelfGuard.Application.Features.AutoService;

/// <summary>
/// Application service interface for the Auto Service module.
/// All operations are tenant-scoped (RLS enforced at DB layer).
/// </summary>
public interface IAutoServiceService
{
    // ── Customers ─────────────────────────────────────────────────────────────

    Task<List<CustomerListItemDto>> GetCustomersAsync(string? search, CancellationToken ct = default);

    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid id, CancellationToken ct = default);

    Task<CustomerDetailDto> CreateCustomerAsync(CustomerCreateDto dto, CancellationToken ct = default);

    Task<(CustomerDetailDto? Customer, string? Error, int? StatusCode)> UpdateCustomerAsync(
        Guid id, CustomerUpdateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes customer. Returns 409 if the customer has vehicles.
    /// </summary>
    Task<(bool Ok, string? Error, int? StatusCode)> DeleteCustomerAsync(
        Guid id, CancellationToken ct = default);

    // ── Vehicles ──────────────────────────────────────────────────────────────

    Task<List<VehicleListItemDto>> GetVehiclesAsync(Guid? customerId, CancellationToken ct = default);

    Task<VehicleDetailDto?> GetVehicleByIdAsync(Guid id, CancellationToken ct = default);

    Task<(VehicleDetailDto? Vehicle, string? Error, int? StatusCode)> CreateVehicleAsync(
        VehicleCreateDto dto, CancellationToken ct = default);

    Task<(VehicleDetailDto? Vehicle, string? Error, int? StatusCode)> UpdateVehicleAsync(
        Guid id, VehicleUpdateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes vehicle. Returns 409 if it has open work orders.
    /// </summary>
    Task<(bool Ok, string? Error, int? StatusCode)> DeleteVehicleAsync(
        Guid id, CancellationToken ct = default);

    // ── Service Catalog ───────────────────────────────────────────────────────

    Task<List<ServiceCatalogItemDto>> GetServiceCatalogAsync(bool includeInactive, CancellationToken ct = default);

    Task<ServiceCatalogItemDto> CreateServiceCatalogItemAsync(
        ServiceCatalogCreateDto dto, CancellationToken ct = default);

    Task<(ServiceCatalogItemDto? Item, string? Error, int? StatusCode)> UpdateServiceCatalogItemAsync(
        Guid id, ServiceCatalogUpdateDto dto, CancellationToken ct = default);

    /// <summary>Soft-delete: sets IsActive = false.</summary>
    Task<(bool Ok, string? Error, int? StatusCode)> DeleteServiceCatalogItemAsync(
        Guid id, CancellationToken ct = default);

    // ── Work Orders ───────────────────────────────────────────────────────────

    Task<List<WorkOrderListItemDto>> GetWorkOrdersAsync(
        string? status, Guid? vehicleId, Guid? mechanicUserId, CancellationToken ct = default);

    Task<WorkOrderDetailDto?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct = default);

    Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> CreateWorkOrderAsync(
        WorkOrderCreateDto dto, Guid tenantId, CancellationToken ct = default);

    Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> UpdateWorkOrderAsync(
        Guid id, WorkOrderUpdateDto dto, CancellationToken ct = default);

    Task<(WorkOrderLineDto? Line, string? Error, int? StatusCode)> AddLineAsync(
        Guid workOrderId, WorkOrderLineCreateDto dto, Guid tenantId, CancellationToken ct = default);

    Task<(bool Ok, string? Error, int? StatusCode)> RemoveLineAsync(
        Guid workOrderId, Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Completes a work order: FEFO write-down of spare parts + stock_events.
    /// Atomic — rolls back on insufficient stock.
    /// </summary>
    Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> CompleteWorkOrderAsync(
        Guid workOrderId, Guid performedBy, Guid tenantId, CancellationToken ct = default);
}
