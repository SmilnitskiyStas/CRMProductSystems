namespace ShelfGuard.Infrastructure.Data;

/// <summary>
/// KI-028 startup canary. Row Level Security is the <b>sole</b> tenant-isolation layer for
/// single-object reads in this codebase — <c>Get*ByIdAsync</c> repository methods deliberately
/// carry no app-level <c>TenantId</c> filter (see CLAUDE.md "Tenant isolation via RLS"). A
/// PostgreSQL role that is a superuser or has <c>BYPASSRLS</c> ignores every RLS policy
/// unconditionally, <c>FORCE ROW LEVEL SECURITY</c> notwithstanding — so if the application ever
/// connects as such a role, all tenant boundaries silently disappear with no error and no log.
/// This exact misconfiguration was found on the staging stack (KI-027). The guard turns that
/// silent, environment-wide data-leak class into a loud, deterministic startup signal.
/// </summary>
public static class RlsRoleGuard
{
    public enum Decision
    {
        /// <summary>Connected role does not bypass RLS — safe.</summary>
        Ok,

        /// <summary>Role bypasses RLS but we are in Development — log CRITICAL, allow boot.</summary>
        WarnDevelopment,

        /// <summary>Role bypasses RLS outside Development — refuse to start.</summary>
        FailFast,
    }

    /// <summary>
    /// Decide how to react to the connected role's RLS-bypass status. Pure function so the policy
    /// is unit-testable without a live database. Development is warned-but-allowed (a fresh clone,
    /// CI, or a not-yet-migrated local box may legitimately still run as a superuser); every other
    /// environment fails fast, because an RLS bypass in a real multi-tenant deployment is a
    /// launch-blocking data-isolation hole.
    /// </summary>
    public static Decision Evaluate(bool roleBypassesRls, bool isDevelopment)
    {
        if (!roleBypassesRls) return Decision.Ok;
        return isDevelopment ? Decision.WarnDevelopment : Decision.FailFast;
    }
}
