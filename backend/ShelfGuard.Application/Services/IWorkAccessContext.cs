namespace ShelfGuard.Application.Services;

public interface IWorkAccessContext
{
    Task<WorkAccessScope?> GetAsync(CancellationToken ct = default);
}

public sealed record WorkAccessScope(Guid TenantId, Guid UserId, string Role, bool TenantWide,
    Guid[] LocationIds, IReadOnlySet<string> Capabilities, IReadOnlyDictionary<string, bool> Permissions)
{
    public bool CanLocation(Guid id) => TenantWide || LocationIds.Contains(id);
    public bool Has(string permission)
    {
        if (Permissions.TryGetValue(permission, out var value)) return value;
        return Role is "enterprise_admin" or "network_manager" or "store_manager" || Capabilities.Contains(permission);
    }
    public bool CanStock => Role is "enterprise_admin" or "network_manager" or "store_manager" or "merchandiser" or "storekeeper";
}

public sealed class WorkAccessDeniedException() : Exception("Access to this operation is denied.");
