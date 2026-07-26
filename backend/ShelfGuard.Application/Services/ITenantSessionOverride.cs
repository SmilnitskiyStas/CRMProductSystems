namespace ShelfGuard.Application.Services;

/// <summary>
/// Lets a narrow, already-trusted operation temporarily run its database writes/reads under a
/// specific tenant's RLS context, for session shapes that structurally never carry an
/// app.tenant_id claim at all (TASK-417). Today that is exactly one case: a consumer-app
/// session (see TenantConnectionInterceptor remarks — a ConsumerAccount JWT is cross-tenant by
/// design and never sets app.tenant_id, so it is always the null-UUID for the whole request).
/// Most tenant-scoped tables (e.g. "customers") only carry the canonical
/// tenant_isolation/provider_bypass/worker_bypass RLS triad — no identity-based policy keyed on
/// app.consumer_account_id the way loyalty_memberships/loyalty_ledger_entries got in TASK-404 —
/// so a consumer session cannot read or write them at all without an explicit override.
///
/// SECURITY CONTRACT — read before adding a new call site: the tenantId passed in must already
/// be a value the caller trusts unconditionally for this specific operation, decided BEFORE
/// calling this method — e.g. an explicit, already-validated route parameter whose business
/// meaning the endpoint has already confirmed is safe (LoyaltyService.JoinAsync's {tenantId}:
/// joining a tenant's loyalty program is a "sign up for this store's bonus card" action, not a
/// privileged one — any consumer may do it for any tenant that has the module enabled). This is
/// NOT a general-purpose "bypass RLS" escape hatch, and it must never be driven by a value an
/// otherwise-untrusted caller could use to read or write a tenant other than the one the calling
/// operation is actually meant to touch.
///
/// Implemented with Postgres SET LOCAL inside an explicit transaction: the override is
/// guaranteed to revert the moment the transaction ends (commit OR rollback), so it can never
/// leak into a query that runs after this call returns, or into a later request reusing the
/// same pooled connection — unlike a plain session-wide SET, which would need a careful manual
/// restore and would fail unsafely (leaking the override) if a caller forgot, or if an exception
/// skipped the restore step.
/// </summary>
public interface ITenantSessionOverride
{
    /// <summary>
    /// Runs <paramref name="action"/> inside a new database transaction with app.tenant_id set
    /// to <paramref name="tenantId"/> for the duration of that transaction only. Any exception
    /// thrown by <paramref name="action"/> rolls back everything it did — callers never have to
    /// worry about a half-written row (e.g. a Customer created without its LoyaltyMembership)
    /// being left behind. Must not be called while the ambient DbContext already has an open
    /// transaction (not needed by any current caller; would require savepoints to nest safely).
    /// </summary>
    Task<T> ExecuteAsync<T>(Guid tenantId, Func<Task<T>> action, CancellationToken ct = default);
}
