using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IReceiptRepository
{
    Task<List<StockReceipt>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    // TASK-640: categoryId/minItems/maxItems — additive category/line-count range filters for
    // the frontend table filter UI. Appended at the very end (still before ct) so no
    // pre-existing parameter's positional index shifts for existing callers.
    Task<(List<StockReceipt> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        string? source = null,
        CancellationToken ct = default);
    Task<StockReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<StockReceiptItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    Task AddAsync(StockReceipt receipt, CancellationToken ct = default);
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(StockReceipt receipt);

    // ── TASK-755: auto replenishment drafts ──────────────────────────────────────────────────────
    /// <summary>Tracked draft receipts of the store with Source auto_replenishment and no TakenByUserId.</summary>
    Task<List<StockReceipt>> GetUntakenAutoDraftsAsync(Guid storeId, CancellationToken ct = default);
    /// <summary>Explicit Add (NOT via the navigation: a client-generated key would be tracked as Modified).</summary>
    Task AddItemAsync(StockReceiptItem item, CancellationToken ct = default);
    void RemoveItems(IEnumerable<StockReceiptItem> items);
    /// <summary>
    /// Atomic compare-and-set: takes a DRAFT receipt for <paramref name="userId"/> only while nobody
    /// holds it (or the same user already does). False = someone else won / not a draft.
    /// </summary>
    Task<bool> TryTakeAsync(Guid id, Guid userId, DateTime takenAt, CancellationToken ct = default);
    /// <summary>Display names of users (for TakenByName).</summary>
    Task<Dictionary<Guid, string>> GetUserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
    void UpdateItem(StockReceiptItem item);

    // ── TASK-757 ─────────────────────────────────────────────────────────────────────────────────
    Task<bool> SupplierExistsAsync(Guid supplierId, CancellationToken ct = default);
    /// <summary>(Exists, default purchase price) of a catalog item of the current tenant.</summary>
    Task<(bool Exists, decimal? PricePurchase)> GetItemPurchaseInfoAsync(Guid productId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
