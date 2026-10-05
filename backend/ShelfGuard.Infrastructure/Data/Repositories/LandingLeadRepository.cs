using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LandingLeadRepository(AppDbContext db) : ILandingLeadRepository
{
    public async Task AddAsync(LandingLead lead, CancellationToken ct) =>
        await db.LandingLeads.AddAsync(lead, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);

    public Task<LandingLead?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.LandingLeads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<(List<LandingLead> Items, int Total)> ListAsync(
        string status, string? search, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<LandingLead> q = db.LandingLeads.AsNoTracking();

        if (status == "new") q = q.Where(l => !l.IsProcessed);
        else if (status == "processed") q = q.Where(l => l.IsProcessed);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = search.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var pattern = "%" + escaped + "%";
            q = q.Where(l =>
                EF.Functions.ILike(l.Name, pattern) ||
                EF.Functions.ILike(l.Phone, pattern) ||
                (l.Company != null && EF.Functions.ILike(l.Company, pattern)));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountUnprocessedAsync(CancellationToken ct) =>
        db.LandingLeads.CountAsync(l => !l.IsProcessed, ct);
}
