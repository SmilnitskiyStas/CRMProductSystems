using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class BeautyTenantLinkRepository(AppDbContext db, ILogger<BeautyTenantLinkRepository> logger) : IBeautyTenantLinkRepository
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
        catch (DbUpdateException ex) when (IsTenantLinkRace(ex))
        {
            // Only the lost race on the unique TenantId index is expected; anything else propagates.
            logger.LogInformation(
                "Beauty link insert lost a race on {Constraint} ({ErrorType}); re-reading the winner.",
                TenantIdIndex, ex.GetType().Name);
            db.Entry(link).State = EntityState.Detached;
            return false;
        }
    }

    internal const string TenantIdIndex = "IX_beauty_tenant_links_TenantId";

    internal static bool IsTenantLinkRace(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TenantIdIndex };

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
