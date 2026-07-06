using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for supplier contract requisites (TASK-316). One row per
/// supplier tenant; standard single-tenant RLS applies via
/// TenantConnectionInterceptor.
/// </summary>
public interface ISupplierContractSettingsRepository
{
    Task<SupplierContractSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(SupplierContractSettings settings, CancellationToken ct = default);

    void Update(SupplierContractSettings settings);

    Task SaveChangesAsync(CancellationToken ct = default);
}
