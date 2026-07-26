using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for <see cref="ConsumerAccount"/> — the global, tenant-less loyalty end-user
/// identity (Loyalty Фаза 0, TASK-405). No RLS applies to this table by design (see the
/// AddLoyaltyProgram migration); every query here is already the full safety boundary.
/// </summary>
public interface IConsumerAccountRepository
{
    Task<ConsumerAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Phone must already be normalized (+380XXXXXXXXX) — globally unique lookup.</summary>
    Task<ConsumerAccount?> GetByPhoneAsync(string phone, CancellationToken ct = default);

    Task AddAsync(ConsumerAccount account, CancellationToken ct = default);
    void Update(ConsumerAccount account);
    Task SaveChangesAsync(CancellationToken ct = default);
}
