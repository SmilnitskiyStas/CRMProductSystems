using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PromotionRuleRepository : IPromotionRuleRepository
{
    private readonly AppDbContext _db;

    public PromotionRuleRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PublishedPromotionRuleRow>> GetPublishedAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var rows = await (
                from r in _db.PromotionRules.AsNoTracking()
                where r.TenantId == tenantId
                      && r.Status == PromotionRule.StatusPublished
                      && r.PublishedVersionId != null
                join v in _db.PromotionRuleVersions.AsNoTracking() on r.PublishedVersionId equals (Guid?)v.Id
                orderby r.CreatedAt, r.Id
                select new { r.Id, r.Name, r.CreatedAt, VersionId = v.Id, v.DefinitionJson })
            .ToListAsync(ct);

        return rows.Select(x => new PublishedPromotionRuleRow(
            x.Id, x.Name, x.CreatedAt, x.VersionId, x.DefinitionJson)).ToList();
    }

    public async Task<IReadOnlyList<PromotionRule>> ListAsync(string? status, CancellationToken ct = default)
    {
        var q = _db.PromotionRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        return await q.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await _db.PromotionRuleVersions.AsNoTracking().Where(v => list.Contains(v.Id)).ToListAsync(ct);
    }

    public async Task RemoveVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await _db.PromotionRuleVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is not null) _db.PromotionRuleVersions.Remove(v);
    }

    public Task<PromotionRule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PromotionRules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsAsync(Guid ruleId, CancellationToken ct = default) =>
        await _db.PromotionRuleVersions.AsNoTracking()
            .Where(v => v.RuleId == ruleId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);

    // Explicit DbSet.Add for Guid-keyed entities (never via a navigation collection).
    public Task AddRuleAsync(PromotionRule rule, CancellationToken ct = default) =>
        _db.PromotionRules.AddAsync(rule, ct).AsTask();

    public Task AddVersionAsync(PromotionRuleVersion version, CancellationToken ct = default) =>
        _db.PromotionRuleVersions.AddAsync(version, ct).AsTask();

    public Task AddApplicationsAsync(IEnumerable<PromotionRuleApplication> applications, CancellationToken ct = default) =>
        _db.PromotionRuleApplications.AddRangeAsync(applications, ct);

    public async Task<IReadOnlyList<PromotionRuleApplication>> GetApplicationsForTransactionAsync(
        Guid transactionId, CancellationToken ct = default) =>
        await _db.PromotionRuleApplications.AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .ToListAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
