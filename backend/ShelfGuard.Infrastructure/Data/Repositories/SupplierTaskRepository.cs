using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the supplier task board (TASK-306). Standard tenant RLS applies
/// via TenantConnectionInterceptor — TenantId is also filtered explicitly for
/// defense in depth and to keep unit tests (InMemory provider, no RLS) meaningful.
/// </summary>
public sealed class SupplierTaskRepository : ISupplierTaskRepository
{
    private readonly AppDbContext _db;

    public SupplierTaskRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(SupplierTask Task, string? AssignedToUserName, string? ClientTenantName)>> GetAllAsync(
        Guid tenantId, Guid? assignedToUserId, Guid? clientTenantId, string? status,
        CancellationToken ct = default)
    {
        var query = _db.SupplierTasks
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId);

        if (assignedToUserId.HasValue)
            query = query.Where(t => t.AssignedToUserId == assignedToUserId.Value);

        if (clientTenantId.HasValue)
            query = query.Where(t => t.ClientTenantId == clientTenantId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                Task = t,
                AssignedToUserName = t.AssignedToUser != null ? t.AssignedToUser.FullName : null,
                ClientTenantName   = t.ClientTenant != null ? t.ClientTenant.Name : null,
            })
            .ToListAsync(ct);

        return rows.Select(r => (r.Task, r.AssignedToUserName, r.ClientTenantName)).ToList();
    }

    public Task<SupplierTask?> GetByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default) =>
        _db.SupplierTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.TenantId == tenantId, ct);

    public async Task AddAsync(SupplierTask task, CancellationToken ct = default) =>
        await _db.SupplierTasks.AddAsync(task, ct);

    public Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);

    public Task<string?> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (string?)t.Name).FirstOrDefaultAsync(ct);

    public Task<bool> UserBelongsToTenantAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<(Guid TenantId, string? Name, int TaskCount, DateTime LastTaskAt)>>
        GetDistinctClientTenantsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _db.SupplierTasks
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ClientTenantId != null)
            .GroupBy(t => t.ClientTenantId!.Value)
            .Select(g => new
            {
                TenantId   = g.Key,
                TaskCount  = g.Count(),
                LastTaskAt = g.Max(t => t.CreatedAt),
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return Array.Empty<(Guid, string?, int, DateTime)>();

        var ids = rows.Select(r => r.TenantId).ToList();
        var names = await _db.Tenants
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return rows
            .Select(r => (r.TenantId, names.TryGetValue(r.TenantId, out var name) ? name : null, r.TaskCount, r.LastTaskAt))
            .ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
