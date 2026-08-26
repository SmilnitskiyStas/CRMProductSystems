using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ICategoryRepository
{
    /// <summary>
    /// Active categories only, ordered by name — the flat lookup list backing
    /// the catalog filter dropdown (TASK-632). No pagination: same reasoning as
    /// <c>ILocationRepository.GetAllAsync</c> — categories are a small per-tenant list.
    /// </summary>
    Task<List<Category>> GetAllActiveAsync(CancellationToken ct = default);
}
