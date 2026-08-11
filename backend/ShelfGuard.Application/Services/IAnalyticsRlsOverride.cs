namespace ShelfGuard.Application.Services;

/// <summary>
/// Lets MarketingAnalyticsRepository's queries run under a session role that store_scope's
/// RESTRICTIVE policy on pos_transactions recognizes as exempt, for the duration of one
/// repository method only (TASK-508/KI-033, ADR-028).
///
/// SECURITY CONTRACT — read before adding a new call site: this is NOT a general-purpose
/// "bypass RLS" escape hatch. It may ONLY be called from inside
/// ShelfGuard.Infrastructure.Data.Repositories.MarketingAnalyticsRepository. The trust boundary
/// it relies on is established once, upstream, before any repository method runs: every
/// MarketingAnalyticsController action requires BOTH
/// [Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)] AND
/// [RequireModule("marketing_analytics")], and every repository query is already scoped to the
/// caller's own JWT tenant_id (tenant_isolation is untouched by this override — it changes
/// app.role only, never app.tenant_id). A future call site outside that repository has NOT
/// inherited that trust boundary just by being in the same codebase — do not reuse this for any
/// other repository or table without re-deriving the same argument from scratch and updating
/// this contract.
///
/// Implemented with Postgres SET LOCAL app.role = 'marketing_analytics_bypass' inside an
/// explicit transaction — reverts automatically on commit or rollback, so it can never leak into
/// a query that runs after this call returns or into a later request reusing the same pooled
/// connection. 'marketing_analytics_bypass' is a value store_scope's own bypass IN-list
/// recognizes; it is not a real role, is never in TenantConnectionInterceptor.ValidRoles, and is
/// never assignable to any User/TenantRole — its only reason to exist is this one bypass check.
/// </summary>
public interface IAnalyticsRlsOverride
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}
