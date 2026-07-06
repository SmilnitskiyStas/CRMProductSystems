using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for supplier↔client cooperation agreements (TASK-316).
/// Rows are visible to either tenant party via the two-tenant
/// tenant_isolation RLS policy (SupplierTenantId OR ClientTenantId matches
/// app.tenant_id) — same model as supplier_chat_sessions.
/// </summary>
public interface ISupplierAgreementRepository
{
    Task<SupplierAgreement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Finds the single live (not rejected/terminated) agreement for a
    /// (supplier, client) pair — mirrors the partial unique index.
    /// </summary>
    Task<SupplierAgreement?> GetForPairAsync(
        Guid supplierTenantId, Guid clientTenantId, CancellationToken ct = default);

    /// <summary>Lists agreements on the supplier side, optionally filtered by status, newest first.</summary>
    Task<IReadOnlyList<SupplierAgreement>> ListForSupplierAsync(
        Guid supplierTenantId, string? status = null, CancellationToken ct = default);

    /// <summary>Lists agreements on the client side, newest first.</summary>
    Task<IReadOnlyList<SupplierAgreement>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    Task AddAsync(SupplierAgreement agreement, CancellationToken ct = default);

    void Update(SupplierAgreement agreement);

    Task SaveChangesAsync(CancellationToken ct = default);
}
