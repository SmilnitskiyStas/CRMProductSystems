namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Attaches an <see cref="Item"/> to a <see cref="Banner"/> as a promoted product shown
/// alongside the banner's detail content (TASK-520). Many-to-many join row with a display
/// order, same shape/style as <see cref="UserLocation"/>/<see cref="BannerLocation"/> — a pure
/// leaf assignment table, nothing references it by <see cref="Id"/>, so detaching a product is
/// a plain hard DELETE; no soft-delete/IsActive flag.
/// </summary>
public sealed class BannerProduct
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Banner this product is attached to.</summary>
    public Guid BannerId { get; private set; }

    /// <summary>Item promoted alongside the banner.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Display order within the banner's attached-product list.</summary>
    public int SortOrder { get; private set; }

    private BannerProduct() { }

    public static BannerProduct Create(Guid tenantId, Guid bannerId, Guid itemId, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        BannerId = bannerId,
        ItemId = itemId,
        SortOrder = sortOrder,
    };
}
