using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for <see cref="ConsumerAccount"/> — the global, tenant-less loyalty end-user
/// identity (Loyalty Фаза 0, TASK-405). No RLS applies to this table by design (see the
/// AddLoyaltyProgram migration); every query here is already the full safety boundary.
///
/// TASK-614: also owns <see cref="ConsumerAccountProfileChange"/> — same combined-repository
/// precedent as <see cref="ILoyaltyRepository"/> pairing <c>LoyaltyMembership</c> with its
/// child <c>LoyaltyLedgerEntry</c>. Profile-change rows carry no RLS either (see that entity's
/// class remarks), and mutation methods below don't call SaveChanges themselves — callers
/// (ConsumerProfileService) stage the <see cref="ConsumerAccount"/> update and its
/// <see cref="ConsumerAccountProfileChange"/> row(s) together and commit both in one
/// <see cref="SaveChangesAsync"/> call.
/// </summary>
public interface IConsumerAccountRepository
{
    Task<ConsumerAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConsumerAccount?> GetByAccountNumberAsync(long accountNumber, CancellationToken ct = default);

    /// <summary>Phone must already be normalized (+380XXXXXXXXX) — globally unique lookup.</summary>
    Task<ConsumerAccount?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<ConsumerAccount?> GetByEmailAsync(string email, CancellationToken ct = default);

    Task AddAsync(ConsumerAccount account, CancellationToken ct = default);
    void Update(ConsumerAccount account);

    /// <summary>Stages the row only — does not call <see cref="SaveChangesAsync"/> itself.</summary>
    Task AddProfileChangeAsync(ConsumerAccountProfileChange change, CancellationToken ct = default);

    /// <summary>Newest first (by <see cref="ConsumerAccountProfileChange.ChangedAt"/>).</summary>
    Task<(List<ConsumerAccountProfileChange> Items, int Total)> GetProfileChangesPagedAsync(
        Guid consumerAccountId, int page, int pageSize, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
