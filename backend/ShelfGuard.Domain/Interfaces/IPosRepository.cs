using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for the POS domain (pos_shifts, pos_transactions, pos_transaction_items).
/// All queries are RLS-scoped: callers must set the tenant context before calling.
/// </summary>
public interface IPosRepository
{
    // ── Shifts ─────────────────────────────────────────────────────────────

    /// <summary>Returns the currently open shift for the tenant (ClosedAt IS NULL), or null.</summary>
    Task<PosShift?> GetOpenShiftAsync(Guid tenantId, CancellationToken ct = default);

    Task<PosShift?> GetShiftByIdAsync(Guid shiftId, CancellationToken ct = default);

    Task AddShiftAsync(PosShift shift, CancellationToken ct = default);

    void UpdateShift(PosShift shift);

    // ── Transactions ────────────────────────────────────────────────────────

    Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default);

    Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default);

    void UpdateTransaction(PosTransaction tx);

    // ── Stock events ────────────────────────────────────────────────────────

    Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default);

    // ── Unit-of-work ────────────────────────────────────────────────────────

    Task SaveChangesAsync(CancellationToken ct = default);
}
