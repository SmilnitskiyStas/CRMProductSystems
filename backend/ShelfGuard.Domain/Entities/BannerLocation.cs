namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Grants a <see cref="Banner"/> visibility at one specific location (TASK-520). Many-to-many
/// join row, same shape/style as <see cref="UserLocation"/> — a banner targets an arbitrary
/// subset of a tenant's locations (never "all locations" implicitly; an admin explicitly picks
/// each one on the banner form). A pure leaf assignment table — nothing references it by
/// <see cref="Id"/> — so removing a target location is a plain hard DELETE; no soft-delete/
/// IsActive flag.
/// </summary>
public sealed class BannerLocation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Banner shown at the location.</summary>
    public Guid BannerId { get; private set; }

    /// <summary>Location the banner is targeted to.</summary>
    public Guid LocationId { get; private set; }

    private BannerLocation() { }

    public static BannerLocation Create(Guid tenantId, Guid bannerId, Guid locationId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        BannerId = bannerId,
        LocationId = locationId,
    };
}
