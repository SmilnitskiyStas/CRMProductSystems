using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IOrderCalcRepository
{
    /// <summary>Buffers for the store (calculation base), with Product included.</summary>
    Task<List<ProductBuffer>> GetBuffersAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Stock on hand per product: SUM(product_stock.Quantity) for the store.</summary>
    Task<Dictionary<Guid, decimal>> GetStockOnHandAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>In transit per product: SUM(QuantityOrdered) of draft receipts into the store.</summary>
    Task<Dictionary<Guid, decimal>> GetInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>MOQ/USQ per product from active product_supplier_settings (primary first).</summary>
    Task<Dictionary<Guid, (decimal Moq, decimal Usq)>> GetMoqUsqAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);
}
