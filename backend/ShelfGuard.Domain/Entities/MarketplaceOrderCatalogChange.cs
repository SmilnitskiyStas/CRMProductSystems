namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Durable record of a barcode set that was auto-merged into the client's own already-linked
/// <see cref="Item"/> when a marketplace order was placed (TASK-697, case 2 — the Item whose
/// <see cref="Item.SourceSupplierItemId"/> already points at the ordered supplier item).
/// Persisted as a jsonb array on <see cref="MarketplaceOrder.CatalogChanges"/> so the client
/// still sees the change when the order is reopened later. Barcodes only — never name / price /
/// category / any non-barcode field.
/// </summary>
public sealed record MarketplaceOrderCatalogChange(
    Guid ItemId,
    string ItemName,
    IReadOnlyList<string> AddedBarcodes,
    bool PrimaryChanged,
    string? NewPrimaryBarcode);
