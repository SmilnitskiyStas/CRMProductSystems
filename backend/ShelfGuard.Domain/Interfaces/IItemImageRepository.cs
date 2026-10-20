using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IItemImageRepository
{
    /// <summary>Images of one item: primary first, then by SortOrder, then CreatedAt.</summary>
    Task<List<ItemImage>> GetByItemAsync(Guid itemId, CancellationToken ct = default);
    Task AddAsync(ItemImage image, CancellationToken ct = default);
    void Remove(ItemImage image);
    Task SaveChangesAsync(CancellationToken ct = default);
}
