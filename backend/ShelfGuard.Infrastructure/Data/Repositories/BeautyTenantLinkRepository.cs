using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class BeautyTenantLinkRepository(AppDbContext db) : IBeautyTenantLinkRepository
{
    public Task<BeautyTenantLink?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct) =>
        db.BeautyTenantLinks.FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);

    public async Task AddAsync(BeautyTenantLink link, CancellationToken ct) =>
        await db.BeautyTenantLinks.AddAsync(link, ct);

    public async Task<bool> TryAddAsync(BeautyTenantLink link, CancellationToken ct)
    {
        await db.BeautyTenantLinks.AddAsync(link, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(link).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
