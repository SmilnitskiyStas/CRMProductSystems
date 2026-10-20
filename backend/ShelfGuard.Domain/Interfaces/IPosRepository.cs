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

    /// <summary>Open shift bound to a specific register (register-scoped shifts, multiple per tenant/store).</summary>
    Task<PosShift?> GetOpenShiftForRegisterAsync(Guid registerId, CancellationToken ct = default);
    /// <summary>Next local shift number: per register, or per tenant for shifts not bound to a register.</summary>
    Task<int> GetNextShiftNumberAsync(Guid tenantId, Guid? registerId, CancellationToken ct = default);

    Task AddShiftAsync(PosShift shift, CancellationToken ct = default);

    void UpdateShift(PosShift shift);

    // ── Transactions ────────────────────────────────────────────────────────

    Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Idempotency lookup: the stored sale for this offline client key, or null.</summary>
    Task<PosTransaction?> GetTransactionByClientIdAsync(Guid tenantId, Guid clientTransactionId, CancellationToken ct = default);

    Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default);

    /// <summary>
    /// TASK-356: sum of TotalAmount for this shift's cash-only transactions (PaymentType
    /// = "cash"). Used for cash-drawer reconciliation at shift close — card payments
    /// never touch the physical drawer, so they must be excluded from the expected-cash
    /// calculation. Returns 0 when the shift has no cash transactions.
    /// </summary>
    Task<decimal> GetCashSalesTotalForShiftAsync(Guid shiftId, CancellationToken ct = default);

    /// <summary>
    /// Returns transactions across all tenants that need fiscalization retries.
    /// Conditions: Status='pending_fiscalization', RetryCount &lt; maxRetries, CreatedAt &lt; cutoff.
    /// Worker-facing (bypasses tenant RLS): caller must set app.role='worker'.
    /// </summary>
    Task<List<PosTransaction>> GetPendingFiscalizationAsync(
        int maxRetries,
        DateTime createdBefore,
        CancellationToken ct = default);

    Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default);

    void UpdateTransaction(PosTransaction tx);

    // ── Stock events ────────────────────────────────────────────────────────

    Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default);

    // ── Unit-of-work ────────────────────────────────────────────────────────

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Drops every pending (unsaved) tracked change. Used by batch sync after a failed item so
    /// its half-applied FEFO decrements can never leak into the next item's SaveChanges.
    /// </summary>
    void DiscardPendingChanges();
}
