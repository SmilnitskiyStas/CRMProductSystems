using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ItemImageRepository : IItemImageRepository
{
    private readonly AppDbContext _db;
    public ItemImageRepository(AppDbContext db) => _db = db;

    public Task<List<ItemImage>> GetByItemAsync(Guid itemId, CancellationToken ct = default) =>
        _db.ItemImages
            .Where(i => i.ItemId == itemId)
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
            .ToListAsync(ct);

    // Explicit DbSet.Add (not nav.Add + Update): avoids EF marking a new row Modified.
    public async Task AddAsync(ItemImage image, CancellationToken ct = default) =>
        await _db.ItemImages.AddAsync(image, ct);

    public void Remove(ItemImage image) => _db.ItemImages.Remove(image);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
