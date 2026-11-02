using ShelfGuard.Application.Services;

namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record InventoryPostingStatus(Guid LocationId, bool Configured, bool Fenced, bool CanAdjust);
public sealed record InventoryPostingDatabaseStatus(bool Fenced, bool LocationActive);
public interface IInventoryPostingStatusReader { Task<InventoryPostingDatabaseStatus> ReadAsync(Guid tenantId, Guid locationId, CancellationToken ct = default); }
public interface IInventoryPostingStatusService { Task<InventoryPostingStatus> GetAsync(Guid locationId, CancellationToken ct = default); }

public sealed class InventoryPostingStatusService(IWorkAccessContext access, IInventoryPostingGate gate,
    IInventoryPostingStatusReader reader) : IInventoryPostingStatusService
{
    public async Task<InventoryPostingStatus> GetAsync(Guid locationId, CancellationToken ct = default)
    {
        var scope = await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        if (locationId == Guid.Empty || !InventoryAdjustmentPlanner.CanAdjust(scope.Role) || !scope.CanLocation(locationId))
            throw new WorkAccessDeniedException();
        var configured = gate.Allows(scope.TenantId, locationId);
        var database = await reader.ReadAsync(scope.TenantId, locationId, ct);
        return new(locationId, configured, database.Fenced, configured && database.Fenced && database.LocationActive);
    }
}
