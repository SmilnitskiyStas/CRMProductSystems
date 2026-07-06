using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier internal task board (TASK-306). Resolves the calling tenant's
/// owner-managed supplier (same helper contract as SupplierCabinetService) so
/// every task is anchored to a real Supplier/Tenant pair.
/// </summary>
public sealed class SupplierTaskService : ISupplierTaskService
{
    public static readonly string[] ValidStatuses = ["pending", "in_progress", "completed", "cancelled"];

    private readonly ISupplierTaskRepository _repo;
    private readonly IMarketplaceRepository _marketplaceRepo;

    public SupplierTaskService(ISupplierTaskRepository repo, IMarketplaceRepository marketplaceRepo)
    {
        _repo            = repo;
        _marketplaceRepo = marketplaceRepo;
    }

    public async Task<(IReadOnlyList<SupplierTaskDto>? Tasks, string? Error)> GetAllAsync(
        Guid tenantId, Guid callerUserId, bool assignedToMe, Guid? clientTenantId, string? status,
        CancellationToken ct = default)
    {
        if (status is not null && !ValidStatuses.Contains(status))
            return (null, $"Invalid status '{status}'. Valid: {string.Join(", ", ValidStatuses)}.");

        var rows = await _repo.GetAllAsync(
            tenantId,
            assignedToUserId: assignedToMe ? callerUserId : null,
            clientTenantId,
            status, ct);

        return (rows.Select(r => Map(r.Task, r.AssignedToUserName, r.ClientTenantName)).ToList(), null);
    }

    public async Task<(SupplierTaskDto? Task, string? Error)> CreateAsync(
        Guid tenantId, Guid createdByUserId, CreateSupplierTaskRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");

        var supplier = await ResolveSupplierAsync(tenantId, ct);
        if (supplier is null)
            return (null, "Supplier cabinet is not available for this tenant.");

        if (request.AssignedToUserId.HasValue &&
            !await _repo.UserBelongsToTenantAsync(tenantId, request.AssignedToUserId.Value, ct))
            return (null, "Assigned user does not belong to this tenant.");

        var task = new SupplierTask
        {
            SupplierId       = supplier.Id,
            TenantId         = tenantId,
            ClientTenantId   = request.ClientTenantId,
            AssignedToUserId = request.AssignedToUserId,
            Title            = request.Title.Trim(),
            Description      = request.Description?.Trim(),
            DueDate          = request.DueDate.HasValue
                ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc)
                : (DateTime?)null,
            CreatedByUserId  = createdByUserId,
        };

        await _repo.AddAsync(task, ct);
        await _repo.SaveChangesAsync(ct);

        return (await MapWithNamesAsync(task, ct), null);
    }

    public async Task<(SupplierTaskDto? Task, string? Error)> UpdateAsync(
        Guid tenantId, Guid taskId, UpdateSupplierTaskRequest request, CancellationToken ct = default)
    {
        var task = await _repo.GetByIdAsync(tenantId, taskId, ct);
        if (task is null)
            return (null, "Task not found.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");

        if (request.AssignedToUserId.HasValue &&
            !await _repo.UserBelongsToTenantAsync(tenantId, request.AssignedToUserId.Value, ct))
            return (null, "Assigned user does not belong to this tenant.");

        task.Title            = request.Title.Trim();
        task.Description      = request.Description?.Trim();
        task.ClientTenantId   = request.ClientTenantId;
        task.AssignedToUserId = request.AssignedToUserId;
        task.DueDate          = request.DueDate.HasValue
            ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        await _repo.SaveChangesAsync(ct);

        return (await MapWithNamesAsync(task, ct), null);
    }

    public async Task<(SupplierTaskDto? Task, string? Error)> UpdateStatusAsync(
        Guid tenantId, Guid taskId, UpdateSupplierTaskStatusRequest request, CancellationToken ct = default)
    {
        if (!ValidStatuses.Contains(request.Status))
            return (null, $"Invalid status '{request.Status}'. Valid: {string.Join(", ", ValidStatuses)}.");

        var task = await _repo.GetByIdAsync(tenantId, taskId, ct);
        if (task is null)
            return (null, "Task not found.");

        task.Status      = request.Status;
        task.CompletedAt = request.Status == "completed" ? DateTime.UtcNow : null;

        await _repo.SaveChangesAsync(ct);

        return (await MapWithNamesAsync(task, ct), null);
    }

    private async Task<Supplier?> ResolveSupplierAsync(Guid tenantId, CancellationToken ct)
    {
        var resolved = await _marketplaceRepo.GetOwnerManagedProfileAsync(tenantId, ct);
        return resolved?.Supplier;
    }

    private async Task<SupplierTaskDto> MapWithNamesAsync(SupplierTask task, CancellationToken ct)
    {
        string? assignedName = task.AssignedToUserId.HasValue
            ? await _repo.GetUserDisplayNameAsync(task.AssignedToUserId.Value, ct)
            : null;
        string? clientName = task.ClientTenantId.HasValue
            ? await _repo.GetTenantDisplayNameAsync(task.ClientTenantId.Value, ct)
            : null;

        return Map(task, assignedName, clientName);
    }

    private static SupplierTaskDto Map(SupplierTask t, string? assignedToUserName, string? clientTenantName) =>
        new(t.Id, t.ClientTenantId, clientTenantName, t.AssignedToUserId, assignedToUserName,
            t.Title, t.Description, t.Status, t.DueDate, t.CreatedByUserId, t.CreatedAt, t.CompletedAt);
}
