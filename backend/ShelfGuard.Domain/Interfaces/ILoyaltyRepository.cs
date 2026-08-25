using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for the tenant-scoped loyalty program tables (Loyalty Фаза 0, TASK-405):
/// <see cref="LoyaltyMembership"/>, <see cref="LoyaltyLedgerEntry"/>,
/// <see cref="LoyaltyProgramSettings"/>. All queries are RLS-scoped: callers must set the
/// tenant context (staff) or app.consumer_account_id (consumer session) before calling —
/// see TenantConnectionInterceptor and the consumer_self_access policy.
///
/// Mutation methods that don't say "SaveChanges" only stage the change on the shared
/// AppDbContext (mirrors IPosRepository) — PosService.CreateSaleAsync relies on this to
/// commit a loyalty accrual/redemption in the SAME transaction as the sale, via its own
/// final SaveChangesAsync. Standalone callers (LoyaltyService) call this interface's own
/// SaveChangesAsync explicitly.
/// </summary>
public interface ILoyaltyRepository
{
    // ── Memberships ──────────────────────────────────────────────────────────

    /// <summary>
    /// TASK-615: includes <see cref="LoyaltyMembership.CurrentTier"/> so callers (PosService's
    /// sale flow above all) always have tier data on hand without a second round-trip.
    /// </summary>
    Task<LoyaltyMembership?> GetMembershipByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>The "does this consumer already have a card at this tenant" / join lookup.</summary>
    Task<LoyaltyMembership?> GetMembershipByTenantConsumerAsync(Guid tenantId, Guid consumerAccountId, CancellationToken ct = default);

    /// <summary>
    /// TASK-618: staff-facing "does this CRM Customer have a loyalty card at all" lookup —
    /// backs the Customers detail view's tier/progress card. Includes
    /// <see cref="LoyaltyMembership.CurrentTier"/>, same as <see cref="GetMembershipByIdAsync"/>.
    /// Returns null when the customer never joined the program (walk-in) — the caller's job to
    /// treat that as "no tier data", not an error.
    /// </summary>
    Task<LoyaltyMembership?> GetMembershipByCustomerIdAsync(Guid customerId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Case 2 (plan §"Кейс 2"): staff member's own membership in their employer's program.</summary>
    Task<LoyaltyMembership?> GetMembershipByLinkedUserAsync(Guid tenantId, Guid linkedUserId, CancellationToken ct = default);

    /// <summary>
    /// Cross-tenant "wallet" read for a consumer session — deliberately NOT scoped by
    /// TenantId (there is none to scope by from this identity's perspective). Safety
    /// boundary is entirely the consumer_self_access RLS policy on ConsumerAccountId.
    /// Includes Tenant for display name.
    /// </summary>
    Task<List<LoyaltyMembership>> GetMembershipsForConsumerAsync(Guid consumerAccountId, CancellationToken ct = default);

    Task AddMembershipAsync(LoyaltyMembership membership, CancellationToken ct = default);
    void UpdateMembership(LoyaltyMembership membership);

    /// <summary>
    /// Atomically claims a TOTP timestep for anti-replay: succeeds (returns true) only when
    /// no earlier call already claimed the same or a later timestep for this membership.
    /// Implemented as a single WHERE-guarded UPDATE (affected-rows check) rather than EF's
    /// optimistic-concurrency token machinery, since LoyaltyMembership carries no
    /// concurrency-token column — the guarded UPDATE is itself atomic at the Postgres row
    /// level, which is all this needs. Commits immediately (own statement), independent of
    /// the caller's other pending changes.
    /// </summary>
    Task<bool> TryClaimTimestepAsync(Guid membershipId, Guid tenantId, long timestep, CancellationToken ct = default);

    // ── Ledger ───────────────────────────────────────────────────────────────

    Task<(List<LoyaltyLedgerEntry> Items, int Total)> GetLedgerPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// TASK-410: batched read of ledger entries tied to a set of POS transactions — backs
    /// PosService.GetSalesForShiftAsync, which needs to backfill accrual/redemption/balance
    /// onto historical sales without issuing one query per row. PosTransaction itself carries
    /// no LoyaltyMembershipId column (only CustomerId), so a transaction's ledger entries
    /// (matched via LoyaltyLedgerEntry.PosTransactionId) are the only persisted signal that
    /// loyalty activity happened on that sale. Returns an empty list without querying when
    /// transactionIds is empty.
    /// </summary>
    Task<List<LoyaltyLedgerEntry>> GetLedgerEntriesForTransactionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default);

    Task AddLedgerEntryAsync(LoyaltyLedgerEntry entry, CancellationToken ct = default);

    // ── Settings (one row per tenant) ────────────────────────────────────────

    Task<LoyaltyProgramSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);
    Task AddSettingsAsync(LoyaltyProgramSettings settings, CancellationToken ct = default);
    void UpdateSettings(LoyaltyProgramSettings settings);

    // ── Tier ladder (TASK-613 schema, TASK-615 service) ─────────────────────

    /// <summary>Ordered ascending by <see cref="LoyaltyTierDefinition.SortOrder"/>.</summary>
    Task<List<LoyaltyTierDefinition>> GetTierLadderAsync(Guid tenantId, CancellationToken ct = default);
    Task AddTierAsync(LoyaltyTierDefinition tier, CancellationToken ct = default);
    void UpdateTier(LoyaltyTierDefinition tier);
    void RemoveTier(LoyaltyTierDefinition tier);

    /// <summary>
    /// Paged, newest-first read of one membership's <see cref="LoyaltyTierChangeHistory"/>.
    /// Includes <c>FromTier</c>/<c>ToTier</c> for display-name resolution.
    /// </summary>
    Task<(List<LoyaltyTierChangeHistory> Items, int Total)> GetTierHistoryPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// TASK-627: stages one append-only <see cref="LoyaltyTierChangeHistory"/> row. Before this,
    /// the table was only ever written by the nightly tier-recompute worker job's own raw SQL
    /// (TypeScript) — this is the first C#-side writer, used when a brand-new
    /// <see cref="LoyaltyMembership"/> is created directly onto the ladder's entry tier instead
    /// of staying tierless until the next recompute run. Mirrors <see cref="AddTierAsync"/>'s
    /// stage-only contract — the caller's own <see cref="SaveChangesAsync"/> commits it.
    /// </summary>
    Task AddTierHistoryAsync(LoyaltyTierChangeHistory history, CancellationToken ct = default);

    // ── Unit-of-work ─────────────────────────────────────────────────────────

    Task SaveChangesAsync(CancellationToken ct = default);
}
