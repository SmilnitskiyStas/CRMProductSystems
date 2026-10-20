using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

// ── Customer activity (analytics page) — raw aggregate rows computed in SQL ───────────────────

public sealed record CustomerActivityStatsRow(
    int ReceiptCount, decimal TotalSpent, DateTime? FirstPurchaseAt, DateTime? LastPurchaseAt,
    int Receipts30d, decimal Spent30d, int Receipts90d, decimal Spent90d, decimal ItemsQuantity);

public sealed record CustomerStoreActivityRow(
    Guid StoreId, string StoreName, int Receipts, decimal Spent, DateTime FirstAt, DateTime LastAt);

public sealed record CustomerMonthActivityRow(int Year, int Month, int Receipts, decimal Spent);

public sealed record CustomerTopProductRow(
    Guid ProductId, string Name, decimal Quantity, decimal Spent, int Receipts, DateTime LastAt);

public sealed record CustomerReceiptLineRow(
    Guid ProductId, string Name, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total);

public sealed record CustomerReceiptRow(
    Guid Id, string ReceiptNumber, string? FiscalNumber, DateTime CreatedAt, Guid StoreId, string StoreName,
    decimal Total, string PaymentType, string Status, IReadOnlyList<CustomerReceiptLineRow> Lines);

public interface ICustomerRepository
{
    Task<CustomerActivityStatsRow> GetActivityStatsAsync(Guid customerId, Guid tenantId, DateTime nowUtc, CancellationToken ct);
    Task<IReadOnlyList<CustomerStoreActivityRow>> GetStoreActivityAsync(Guid customerId, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CustomerMonthActivityRow>> GetMonthlyActivityAsync(Guid customerId, Guid tenantId, DateTime fromUtc, CancellationToken ct);
    Task<IReadOnlyList<CustomerTopProductRow>> GetTopProductsAsync(Guid customerId, Guid tenantId, int take, CancellationToken ct);
    Task<(IReadOnlyList<CustomerReceiptRow> Items, int Total)> GetReceiptsPagedAsync(
        Guid customerId, Guid tenantId, int page, int pageSize, CancellationToken ct);

    Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task<(List<Customer> Items, int Total)> GetPagedAsync(Guid tenantId, int page, int pageSize, string? search, CancellationToken ct);
    Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct);

    /// <summary>
    /// TASK-405 (Loyalty Фаза 0): finds a tenant's existing Customer by exact phone match —
    /// used to auto-link a LoyaltyMembership to the tenant's own CRM record instead of
    /// always creating a new one. Phone comparison is exact/tenant-scoped, same as
    /// <see cref="ExistsByPhoneAsync"/> — the caller is responsible for normalizing the
    /// phone consistently before calling (ConsumerAccount.Phone is already normalized).
    /// </summary>
    Task<Customer?> FindByPhoneAsync(string phone, Guid tenantId, CancellationToken ct);
    Task<Customer> CreateAsync(Customer customer, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct);
}
