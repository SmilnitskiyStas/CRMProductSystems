using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ITelegramLinkRepository
{
    /// <summary>Marks all unused, unexpired codes of the user as used (single-active-code rule).</summary>
    Task InvalidateActiveCodesAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(TelegramLinkCode code, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
