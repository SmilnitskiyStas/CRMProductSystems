namespace ShelfGuard.Application.Services;

/// <summary>
/// Lets MarketplaceRepository's cross-tenant marketplace reads run under Postgres
/// <c>app.role = 'provider'</c> — the value the <c>provider_bypass</c> RLS policy recognizes —
/// for the duration of one repository operation's own transaction only
/// (TASK-643/KI-036, ADR-035).
///
/// SECURITY CONTRACT — read before adding a new call site. This is NOT a general-purpose
/// "bypass RLS" escape hatch:
///
/// 1. It may ONLY be called from inside
///    ShelfGuard.Infrastructure.Data.Repositories.MarketplaceRepository. A call site anywhere
///    else has NOT inherited that repository's trust boundary just by being in the same
///    codebase. A unit test (ProviderRlsOverrideContainmentTests) asserts no other type takes
///    this interface as a constructor parameter — if you are about to make it fail, re-derive
///    the argument below from scratch and update this contract first.
///
/// 2. BLAST RADIUS — measured, not estimated: <b>107 tables carry a <c>provider_bypass</c>
///    policy</b> (<c>SELECT count(*) FROM pg_policies WHERE policyname = 'provider_bypass'</c>,
///    verified 2026-08-30). Those policies are PERMISSIVE <c>FOR ALL</c> with
///    <c>WITH CHECK = NULL</c>, which Postgres defaults to the <c>USING</c> expression — so
///    <c>'provider'</c> is a full cross-tenant <b>read AND write</b> bypass across the whole
///    schema, NOT "the ~8 marketplace tables". Do not read this override as narrowly scoped by
///    table; it is narrow only in <i>duration</i>.
///
/// 3. INVARIANT that makes (2) acceptable: each ExecuteAsync block wraps exactly one repository
///    operation and makes NO call out to another service, repository,
///    <see cref="ITenantSessionOverride"/> or <see cref="IAnalyticsRlsOverride"/>. Everything
///    inside the lambda must be a query (or a query + its own SaveChangesAsync) issued directly
///    against the marketplace tables by the repository itself.
///
/// 4. It must NEVER be invoked from inside an <see cref="ITenantSessionOverride"/> or
///    <see cref="IAnalyticsRlsOverride"/> lambda: those already own an ambient transaction, and
///    this override deliberately does not join one — EF will throw
///    <c>InvalidOperationException</c> loudly rather than silently widen the outer transaction's
///    RLS context.
///
/// 5. It changes <c>app.role</c> only, never <c>app.tenant_id</c> — tenant_isolation's own
///    session variable is untouched.
///
/// Implemented with Postgres <c>SET LOCAL app.role = 'provider'</c> inside an explicit
/// transaction, so it reverts automatically on commit, rollback or an unhandled exception, and
/// can never leak into a query that runs after the call returns. It replaces the session-level
/// <c>SET app.role = 'provider'</c> that MarketplaceRepository used to issue on a manually
/// opened DbConnection and never reset (KI-036) — that leak was bounded to a single HTTP
/// request only by Npgsql's default <c>DISCARD ALL</c> pool reset on connection return
/// (<c>No Reset On Close=false</c>); nothing in the application enforced it.
///
/// Unlike <see cref="IAnalyticsRlsOverride"/>'s 'marketing_analytics_bypass', <c>'provider'</c>
/// IS a real assignable role value (TenantConnectionInterceptor sets it session-wide for
/// provider JWTs). It is kept rather than replaced with a dedicated sentinel because this change
/// only narrows the duration of an already-existing bypass; a sentinel is recorded in ADR-035 as
/// deferred hardening, to be revisited the moment rule (1) or (3) above is relaxed.
/// </summary>
public interface IProviderRlsOverride
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}
