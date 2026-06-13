using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PosRepository : IPosRepository
{
    private readonly AppDbContext _db;

    public PosRepository(AppDbContext db) => _db = db;

    // ── Shifts ──────────────────────────────────────────────────────────────

    public Task<PosShift?> GetOpenShiftAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.PosShifts
            .Include(s => s.Store)
            .Include(s => s.Cashier)
            .Where(s => s.TenantId == tenantId && s.ClosedAt == null)
            .FirstOrDefaultAsync(ct);

    public Task<PosShift?> GetShiftByIdAsync(Guid shiftId, CancellationToken ct = default) =>
        _db.PosShifts
            .Include(s => s.Store)
            .Include(s => s.Cashier)
            .FirstOrDefaultAsync(s => s.Id == shiftId, ct);

    public Task AddShiftAsync(PosShift shift, CancellationToken ct = default) =>
        _db.PosShifts.AddAsync(shift, ct).AsTask();

    public void UpdateShift(PosShift shift) => _db.PosShifts.Update(shift);

    // ── Transactions ────────────────────────────────────────────────────────

    public Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PosTransactions
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        _db.PosTransactions
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .Where(t => t.ShiftId == shiftId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default) =>
        _db.PosTransactions.AddAsync(tx, ct).AsTask();

    public void UpdateTransaction(PosTransaction tx) => _db.PosTransactions.Update(tx);

    // ── Stock events ────────────────────────────────────────────────────────

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) =>
        _db.StockEvents.AddAsync(ev, ct).AsTask();

    // ── Unit-of-work ────────────────────────────────────────────────────────

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
