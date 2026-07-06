using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for B2B marketplace orders (TASK-316). Two-tenant RLS: both
/// the supplier and the client tenant see the same order rows.
/// </summary>
public interface IMarketplaceOrderRepository
{
    /// <summary>Loads an order including its line items.</summary>
    Task<MarketplaceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists orders on the supplier side, newest first (items included).</summary>
    Task<IReadOnlyList<MarketplaceOrder>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>Lists orders on the client side, newest first (items included).</summary>
    Task<IReadOnlyList<MarketplaceOrder>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    Task AddAsync(MarketplaceOrder order, CancellationToken ct = default);

    void Update(MarketplaceOrder order);

    /// <summary>Total number of orders ever placed with a supplier — used to generate the next OrderNumber.</summary>
    Task<int> CountForSupplierAsync(Guid supplierTenantId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
