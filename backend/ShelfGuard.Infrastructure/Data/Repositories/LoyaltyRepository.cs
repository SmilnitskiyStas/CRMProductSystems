using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LoyaltyRepository : ILoyaltyRepository
{
    private readonly AppDbContext _db;

    public LoyaltyRepository(AppDbContext db) => _db = db;

    // ── Memberships ──────────────────────────────────────────────────────────

    public Task<LoyaltyMembership?> GetMembershipByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        _db.LoyaltyMemberships.Include(m => m.CurrentTier)
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);

    public Task<LoyaltyMembership?> GetMembershipByTenantConsumerAsync(
        Guid tenantId, Guid consumerAccountId, CancellationToken ct = default) =>
        _db.LoyaltyMemberships.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.ConsumerAccountId == consumerAccountId, ct);

    public Task<LoyaltyMembership?> GetMembershipByCustomerIdAsync(
        Guid customerId, Guid tenantId, CancellationToken ct = default) =>
        _db.LoyaltyMemberships.Include(m => m.CurrentTier)
            .FirstOrDefaultAsync(m => m.CustomerId == customerId && m.TenantId == tenantId, ct);

    public Task<LoyaltyMembership?> GetMembershipByLinkedUserAsync(
        Guid tenantId, Guid linkedUserId, CancellationToken ct = default) =>
        _db.LoyaltyMemberships.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.LinkedUserId == linkedUserId, ct);

    public Task<List<LoyaltyMembership>> GetMembershipsForConsumerAsync(
        Guid consumerAccountId, CancellationToken ct = default) =>
        _db.LoyaltyMemberships
            .Include(m => m.Tenant)
            // Deliberately NOT filtered by TenantId — cross-tenant "wallet" read, see
            // interface doc. The safety boundary is the consumer_self_access RLS policy.
            .Where(m => m.ConsumerAccountId == consumerAccountId)
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync(ct);

    public Task AddMembershipAsync(LoyaltyMembership membership, CancellationToken ct = default) =>
        _db.LoyaltyMemberships.AddAsync(membership, ct).AsTask();

    public void UpdateMembership(LoyaltyMembership membership) => _db.LoyaltyMemberships.Update(membership);

    public async Task<bool> TryClaimTimestepAsync(
        Guid membershipId, Guid tenantId, long timestep, CancellationToken ct = default)
    {
        // Single WHERE-guarded UPDATE — atomic at the Postgres row level without needing a
        // concurrency-token column (see ILoyaltyRepository doc). Two concurrent scans of the
        // same code race on this row; Postgres serializes them, and the second to commit
        // re-evaluates the WHERE clause against the now-updated row and affects 0 rows.
        // ExecuteSqlInterpolatedAsync parameterizes every {} value — never string-concatenated.
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE loyalty_memberships
               SET ""LastRedeemedTimestep"" = {timestep}
               WHERE ""Id"" = {membershipId}
                 AND ""TenantId"" = {tenantId}
                 AND (""LastRedeemedTimestep"" IS NULL OR ""LastRedeemedTimestep"" < {timestep})",
            ct);

        return rows == 1;
    }

    // ── Ledger ───────────────────────────────────────────────────────────────

    public async Task<(List<LoyaltyLedgerEntry> Items, int Total)> GetLedgerPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.LoyaltyLedgerEntries
            .Where(e => e.TenantId == tenantId && e.MembershipId == membershipId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<List<LoyaltyLedgerEntry>> GetLedgerEntriesForTransactionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default)
    {
        if (transactionIds.Count == 0)
            return Task.FromResult(new List<LoyaltyLedgerEntry>());

        return _db.LoyaltyLedgerEntries
            .Where(e => e.TenantId == tenantId
                && e.PosTransactionId != null
                && transactionIds.Contains(e.PosTransactionId!.Value))
            .ToListAsync(ct);
    }

    public Task AddLedgerEntryAsync(LoyaltyLedgerEntry entry, CancellationToken ct = default) =>
        _db.LoyaltyLedgerEntries.AddAsync(entry, ct).AsTask();

    // ── Settings ─────────────────────────────────────────────────────────────

    public Task<LoyaltyProgramSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.LoyaltyProgramSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public Task AddSettingsAsync(LoyaltyProgramSettings settings, CancellationToken ct = default) =>
        _db.LoyaltyProgramSettings.AddAsync(settings, ct).AsTask();

    public void UpdateSettings(LoyaltyProgramSettings settings) => _db.LoyaltyProgramSettings.Update(settings);

    // ── Tier ladder (TASK-613 schema, TASK-615 service) ─────────────────────

    public Task<List<LoyaltyTierDefinition>> GetTierLadderAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.LoyaltyTierDefinitions
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

    public Task AddTierAsync(LoyaltyTierDefinition tier, CancellationToken ct = default) =>
        _db.LoyaltyTierDefinitions.AddAsync(tier, ct).AsTask();

    public void UpdateTier(LoyaltyTierDefinition tier) => _db.LoyaltyTierDefinitions.Update(tier);

    public void RemoveTier(LoyaltyTierDefinition tier) => _db.LoyaltyTierDefinitions.Remove(tier);

    public async Task<(List<LoyaltyTierChangeHistory> Items, int Total)> GetTierHistoryPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.LoyaltyTierChangeHistories
            .Include(h => h.FromTier)
            .Include(h => h.ToTier)
            .Where(h => h.TenantId == tenantId && h.MembershipId == membershipId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(h => h.ChangedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // ── Unit-of-work ─────────────────────────────────────────────────────────

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // TASK-414: LoyaltyMembership.Balance carries an xmin concurrency token (see
            // AppDbContext) — this fires when two writers raced on the same membership (e.g.
            // two POS redemptions, or a POS redemption racing a staff ManualAdjustAsync call).
            // Translate to a Domain-level exception, same shape as PosRepository, so
            // Application services (which must not reference EF Core) can catch it.
            throw new ConcurrencyConflictException(
                "One or more rows were modified concurrently by another operation.", ex);
        }
    }
}
