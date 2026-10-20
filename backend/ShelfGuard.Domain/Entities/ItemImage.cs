namespace ShelfGuard.Domain.Entities;

/// <summary>
/// One of several photos of a catalog <see cref="Item"/> (different sides of the product), used as
/// AI reference photos. Exactly one image per item is primary; <c>Item.ImageUrl</c> mirrors it.
/// </summary>
public sealed class ItemImage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ItemId { get; init; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Item? Item { get; init; }
}
