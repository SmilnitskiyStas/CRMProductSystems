using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IAiOrderRepository
{
    Task<List<AiOrderSuggestion>> GetListAsync(Guid? storeId, int limit, CancellationToken ct = default);
    Task<AiOrderSuggestion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AiOrderSuggestionItem?> GetItemAsync(Guid suggestionId, Guid itemId, CancellationToken ct = default);

    /// <summary>ADU map for prompt context (adu_30d per product).</summary>
    Task<Dictionary<Guid, decimal?>> GetAdu30Async(Guid storeId, CancellationToken ct = default);

    Task<string?> GetStoreNameAsync(Guid storeId, CancellationToken ct = default);

    Task AddAsync(AiOrderSuggestion suggestion, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
