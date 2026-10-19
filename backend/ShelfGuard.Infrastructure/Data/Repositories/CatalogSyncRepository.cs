using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CatalogSyncRepository : ICatalogSyncRepository
{
    private readonly AppDbContext _db;

    public CatalogSyncRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CatalogSyncRow>> GetChangedSinceAsync(
        DateTime? since, Guid? afterId, int limit, CancellationToken ct = default)
    {
        var q = _db.Items.AsNoTracking().AsQueryable();
        if (since.HasValue)
        {
            var s = since.Value;
            q = afterId.HasValue
                ? q.Where(i => i.UpdatedAt > s || (i.UpdatedAt == s && i.Id.CompareTo(afterId.Value) > 0))
                : q.Where(i => i.UpdatedAt > s);
        }

        var rows = await q
            .OrderBy(i => i.UpdatedAt).ThenBy(i => i.Id)
            .Take(limit + 1)
            .Select(i => new { i.Id, i.Name, i.Barcodes, i.PriceRetail, i.VatRate, i.Unit, i.IsActive, i.UpdatedAt })
            .ToListAsync(ct);

        return rows.Select(i => new CatalogSyncRow(
            i.Id, i.Name, i.Barcodes, i.PriceRetail, i.VatRate, i.Unit, i.IsActive, i.UpdatedAt)).ToList();
    }
}
