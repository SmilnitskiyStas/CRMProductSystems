using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier internal task board (TASK-306, plan calm-singing-marble.md Part 4).
/// A standalone entity — not linked to existing service-desk tickets or orders.
/// Every operation is scoped to the calling supplier tenant.
/// </summary>
public interface ISupplierTaskService
{
    Task<(IReadOnlyList<SupplierTaskDto>? Tasks, string? Error)> GetAllAsync(
        Guid tenantId, Guid callerUserId, bool assignedToMe, Guid? clientTenantId, string? status,
        CancellationToken ct = default);

    Task<(SupplierTaskDto? Task, string? Error)> CreateAsync(
        Guid tenantId, Guid createdByUserId, CreateSupplierTaskRequest request, CancellationToken ct = default);

    Task<(SupplierTaskDto? Task, string? Error)> UpdateAsync(
        Guid tenantId, Guid taskId, UpdateSupplierTaskRequest request, CancellationToken ct = default);

    Task<(SupplierTaskDto? Task, string? Error)> UpdateStatusAsync(
        Guid tenantId, Guid taskId, UpdateSupplierTaskStatusRequest request, CancellationToken ct = default);
}
