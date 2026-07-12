using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for temporary per-user permission grants (ADR-019, TASK-341).
/// Tenant-scoped methods rely on RLS (TenantConnectionInterceptor) plus an explicit
/// TenantId filter for defense in depth (mirrors SupplierTaskRepository). The
/// cross-tenant scans (<see cref="GetExpiringSoonAsync"/>/<see cref="GetJustExpiredAsync"/>
/// with tenantId = null) expect a provider-bypass DB session, same pattern as
/// ProviderTicketRepository.
/// </summary>
public sealed class UserPermissionGrantRepository : IUserPermissionGrantRepository
{
    private readonly AppDbContext _db;

    public UserPermissionGrantRepository(AppDbContext db) => _db = db;

    public Task<IReadOnlyList<UserPermissionGrant>> GetActiveGrantsForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return QueryAsList(_db.UserPermissionGrants
            .AsNoTracking()
            .Where(g =>
                g.TenantId == tenantId &&
                g.UserId == userId &&
                g.RevokedAt == null &&
                g.ExpiresAt > now), ct);
    }

    public Task<UserPermissionGrant?> GetByIdAsync(
        Guid tenantId, Guid grantId, CancellationToken ct = default) =>
        _db.UserPermissionGrants.FirstOrDefaultAsync(
            g => g.Id == grantId && g.TenantId == tenantId, ct);

    public async Task AddAsync(UserPermissionGrant grant, CancellationToken ct = default) =>
        await _db.UserPermissionGrants.AddAsync(grant, ct);

    public async Task<bool> RevokeAsync(
        Guid tenantId, Guid grantId, Guid revokedByUserId, CancellationToken ct = default)
    {
        var grant = await _db.UserPermissionGrants
            .FirstOrDefaultAsync(g => g.Id == grantId && g.TenantId == tenantId, ct);

        if (grant is null || grant.RevokedAt is not null)
            return false;

        grant.Revoke(revokedByUserId);
        return true;
    }

    public Task<IReadOnlyList<UserPermissionGrant>> GetExpiringSoonAsync(
        Guid? tenantId, TimeSpan window, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var horizon = now.Add(window);

        var query = _db.UserPermissionGrants
            .AsNoTracking()
            .Where(g =>
                g.RevokedAt == null &&
                g.NotifiedExpiringAt == null &&
                g.ExpiresAt > now &&
                g.ExpiresAt <= horizon);

        if (tenantId.HasValue)
            query = query.Where(g => g.TenantId == tenantId.Value);

        return QueryAsList(query, ct);
    }

    public Task<IReadOnlyList<UserPermissionGrant>> GetJustExpiredAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var query = _db.UserPermissionGrants
            .AsNoTracking()
            .Where(g =>
                g.RevokedAt == null &&
                g.NotifiedExpiredAt == null &&
                g.ExpiresAt <= now);

        if (tenantId.HasValue)
            query = query.Where(g => g.TenantId == tenantId.Value);

        return QueryAsList(query, ct);
    }

    public async Task MarkNotifiedExpiringAsync(Guid grantId, CancellationToken ct = default)
    {
        var grant = await _db.UserPermissionGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        grant?.MarkNotifiedExpiring();
    }

    public async Task MarkNotifiedExpiredAsync(Guid grantId, CancellationToken ct = default)
    {
        var grant = await _db.UserPermissionGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        grant?.MarkNotifiedExpired();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    private static async Task<IReadOnlyList<UserPermissionGrant>> QueryAsList(
        IQueryable<UserPermissionGrant> query, CancellationToken ct) =>
        await query.OrderBy(g => g.ExpiresAt).ToListAsync(ct);
}
