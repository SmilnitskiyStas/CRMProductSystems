using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>Authorization/module filters may track read entities in the request context. The command owns a clean unit of work.</summary>
public sealed class InventoryAdjustmentCommandStore(DbContextOptions<AppDbContext> options, IInventoryPostingGate gate,
    TimeProvider clock) : IInventoryAdjustmentStore
{
    public async Task<InventoryAdjustmentReceipt> PostAsync(InventoryAdjustmentPlan plan, CancellationToken ct = default)
    {
        await using var db = new AppDbContext(options);
        return await new InventoryAdjustmentStore(db, gate, clock).PostAsync(plan, ct);
    }
}
