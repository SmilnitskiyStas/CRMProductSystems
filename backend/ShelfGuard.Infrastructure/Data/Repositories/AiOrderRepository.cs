using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class AiOrderRepository : IAiOrderRepository
{
    private readonly AppDbContext _db;

    public AiOrderRepository(AppDbContext db) => _db = db;

    public Task<List<AiOrderSuggestion>> GetListAsync(Guid? storeId, int limit, CancellationToken ct = default)
    {
        var query = _db.AiOrderSuggestions
            .Include(s => s.Store)
            .AsQueryable();

        if (storeId.HasValue) query = query.Where(s => s.StoreId == storeId);

        return query
            .OrderByDescending(s => s.GeneratedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<AiOrderSuggestion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AiOrderSuggestions
            .Include(s => s.Store)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<AiOrderSuggestionItem?> GetItemAsync(Guid suggestionId, Guid itemId, CancellationToken ct = default) =>
        _db.AiOrderSuggestionItems
            .Include(i => i.Suggestion)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.SuggestionId == suggestionId, ct);

    public async Task<Dictionary<Guid, decimal?>> GetAdu30Async(Guid storeId, CancellationToken ct = default)
    {
        return await _db.ProductAdus
            .Where(a => a.StoreId == storeId)
            .ToDictionaryAsync(a => a.ProductId, a => a.Adu30d, ct);
    }

    public Task<string?> GetStoreNameAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.Where(s => s.Id == storeId).Select(s => (string?)s.Name).FirstOrDefaultAsync(ct);

    public async Task AddAsync(AiOrderSuggestion suggestion, CancellationToken ct = default) =>
        await _db.AiOrderSuggestions.AddAsync(suggestion, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
