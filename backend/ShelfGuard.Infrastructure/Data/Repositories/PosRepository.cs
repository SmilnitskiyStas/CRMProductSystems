using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
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
            // TASK-410: needed so PosService.GetSalesForShiftAsync can map CustomerName onto
            // SaleDto without a separate per-row customer lookup (Customer is a simple FK,
            // same Include shape already used for Items/Product on this same query).
            .Include(t => t.Customer)
            .Where(t => t.ShiftId == shiftId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<decimal> GetCashSalesTotalForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        _db.PosTransactions
            .Where(t => t.ShiftId == shiftId && t.PaymentType == "cash")
            .SumAsync(t => t.TotalAmount, ct);

    public Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default) =>
        _db.PosTransactions.AddAsync(tx, ct).AsTask();

    public void UpdateTransaction(PosTransaction tx) => _db.PosTransactions.Update(tx);

    public Task<List<PosTransaction>> GetPendingFiscalizationAsync(
        int maxRetries,
        DateTime createdBefore,
        CancellationToken ct = default) =>
        _db.PosTransactions
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .Where(t =>
                t.Status == "pending_fiscalization" &&
                t.RetryCount < maxRetries &&
                t.CreatedAt < createdBefore)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

    // ── Stock events ────────────────────────────────────────────────────────

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) =>
        _db.StockEvents.AddAsync(ev, ct).AsTask();

    // ── Unit-of-work ────────────────────────────────────────────────────────

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // ProductStock.Quantity carries an xmin concurrency token (TASK-356) — this
            // fires when two writers raced on the same batch (e.g. two POS sales
            // consuming the last unit at once). Translate to a Domain-level exception so
            // Application services (which must not reference EF Core) can catch it.
            throw new ConcurrencyConflictException(
                "One or more rows were modified concurrently by another operation.", ex);
        }
    }
}
