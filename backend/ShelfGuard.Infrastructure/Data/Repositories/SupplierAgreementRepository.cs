using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Cooperation agreements (TASK-316). Two-tenant tenant_isolation RLS
/// (SupplierTenantId OR ClientTenantId = app.tenant_id) lets either party's
/// tenant context read/write its own agreements — same model as
/// supplier_chat_sessions, no provider-role bypass needed.
/// </summary>
public sealed class SupplierAgreementRepository : ISupplierAgreementRepository
{
    private readonly AppDbContext _db;

    public SupplierAgreementRepository(AppDbContext db) => _db = db;

    public Task<SupplierAgreement?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SupplierAgreements.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<SupplierAgreement?> GetForPairAsync(
        Guid supplierTenantId, Guid clientTenantId, CancellationToken ct = default) =>
        _db.SupplierAgreements.FirstOrDefaultAsync(
            a => a.SupplierTenantId == supplierTenantId
                 && a.ClientTenantId == clientTenantId
                 && a.Status != SupplierAgreementStatus.Rejected
                 && a.Status != SupplierAgreementStatus.Terminated,
            ct);

    public async Task<IReadOnlyList<SupplierAgreement>> ListForSupplierAsync(
        Guid supplierTenantId, string? status = null, CancellationToken ct = default)
    {
        var query = _db.SupplierAgreements.AsNoTracking()
            .Where(a => a.SupplierTenantId == supplierTenantId);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);
        return await query.OrderByDescending(a => a.RequestedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SupplierAgreement>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default) =>
        await _db.SupplierAgreements.AsNoTracking()
            .Where(a => a.ClientTenantId == clientTenantId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SupplierAgreement agreement, CancellationToken ct = default) =>
        await _db.SupplierAgreements.AddAsync(agreement, ct);

    public void Update(SupplierAgreement agreement) =>
        _db.SupplierAgreements.Update(agreement);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
