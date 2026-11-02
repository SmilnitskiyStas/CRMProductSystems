using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class InventoryPostingStatusReader(AppDbContext db) : IInventoryPostingStatusReader
{
    public async Task<InventoryPostingDatabaseStatus> ReadAsync(Guid tenantId, Guid locationId, CancellationToken ct = default)
    {
        var fenced = await IntegratedScopeReadiness.Ready(db,tenantId,locationId,ct);
        var active = await db.Locations.AsNoTracking().AnyAsync(l => l.Id == locationId && l.TenantId == tenantId && l.IsActive, ct);
        return new(fenced, active);
    }
}
