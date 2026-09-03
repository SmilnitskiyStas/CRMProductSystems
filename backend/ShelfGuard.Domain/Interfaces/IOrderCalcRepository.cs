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

    /// <summary>
    /// In transit per product from OPEN B2B marketplace orders headed to this store (Phase 4,
    /// plan D5) — the double-order fix. Sums <c>marketplace_order_items.Qty</c> of orders in
    /// status new/confirmed/shipped whose <c>DestinationStoreId</c> is this store, mapping the
    /// supplier's <c>SupplierItemId</c> onto the buyer's catalog through
    /// <c>Item.SourceSupplierItemId</c>. A line whose snapshot unit differs from the buyer
    /// item's unit is EXCLUDED (plan п.2 — avoids e.g. a 12× skew when the order was in "boxes"
    /// and the catalog item is "each"). Keyed on the resulting buyer <c>Item.Id</c>, so it adds
    /// straight onto <see cref="GetInTransitAsync"/>'s result.
    /// </summary>
    Task<Dictionary<Guid, decimal>> GetOpenMarketplaceInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, Guid tenantId, CancellationToken ct = default);

    /// <summary>MOQ/USQ per product from active product_supplier_settings (primary first).</summary>
    Task<Dictionary<Guid, (decimal Moq, decimal Usq)>> GetMoqUsqAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);
}
