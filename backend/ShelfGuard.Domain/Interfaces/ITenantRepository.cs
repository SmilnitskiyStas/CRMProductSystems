using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Tenant data access. RLS-scoped per the caller's connection role: a provider-role caller
/// (ProviderService) sees all tenants via the provider_bypass policy; any other role sees
/// only its own tenant row via tenant_isolation (e.g. ModulesSettingsService, RequireModuleFilter).
/// </summary>
public interface ITenantRepository
{
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct);
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);

    // Cross-tenant counts (efficient group queries — no N+1)
    Task<Dictionary<Guid, int>> GetUserCountsAsync(CancellationToken ct);
    Task<Dictionary<Guid, int>> GetStoreCountsAsync(CancellationToken ct);
    Task<Dictionary<Guid, int>> GetExpiredBatchCountsAsync(CancellationToken ct);

    // Aggregate health metrics
    Task<int> GetTotalUsersAsync(CancellationToken ct);
    Task<int> GetTotalExpiredBatchesAsync(CancellationToken ct);

    // Tenant mutations
    Task AddAsync(Tenant tenant, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    // v4.1 supplier self-service onboarding hook (ADR-016, TASK-289).
    // Deferred variant of AddAsync — does not call SaveChanges itself, so the tenant
    // and its Supplier/SupplierProfile pair (added via AddSupplierAsync/AddSupplierProfileAsync)
    // commit together in one SaveChangesAsync call.
    Task AddPendingAsync(Tenant tenant, CancellationToken ct);
    Task AddSupplierAsync(Supplier supplier, CancellationToken ct);
    Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct);
}
