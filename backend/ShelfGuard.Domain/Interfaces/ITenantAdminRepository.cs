using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Cross-tenant data access for the SaaS Admin Panel.
/// Bypasses RLS — only used by TenantAdminService (role = provider).
/// </summary>
public interface ITenantAdminRepository
{
    // Tenant CRUD
    Task<IReadOnlyList<Tenant>> GetAllTenantsAsync(CancellationToken ct);
    Task<Tenant?> GetTenantByIdAsync(Guid id, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task AddTenantAsync(Tenant tenant, CancellationToken ct);
    Task AddUserAsync(User user, CancellationToken ct);

    // v4.1 supplier self-service onboarding hook (ADR-016)
    Task AddSupplierAsync(Supplier supplier, CancellationToken ct);
    Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    // Usage statistics (per-tenant counts)
    Task<int> CountUsersAsync(Guid tenantId, CancellationToken ct);
    Task<int> CountStoresAsync(Guid tenantId, CancellationToken ct);
    Task<int> CountProductsAsync(Guid tenantId, CancellationToken ct);
    Task<int> CountSalesLast30DaysAsync(Guid tenantId, CancellationToken ct);
}
