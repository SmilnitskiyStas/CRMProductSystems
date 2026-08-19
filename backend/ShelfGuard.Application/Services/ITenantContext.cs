namespace ShelfGuard.Application.Services;

/// <summary>
/// Centrally resolves the current tenant id for an authenticated staff request (TASK-528).
/// Replaces the per-controller <c>ResolveTenantId()</c>/<c>GetTenantId()</c> duplication that
/// used to read <c>User.FindFirst("tenant_id")</c> directly in every controller — see
/// <c>docs/architecture/CURRENT_STATE.md</c> §1. New controllers/services should inject this
/// instead of touching <c>ClaimsPrincipal</c> themselves.
///
/// <see cref="TenantId"/> is <c>null</c> whenever the request has no valid <c>tenant_id</c>
/// claim: unauthenticated requests, provider/consumer sessions (which never carry the claim —
/// see <c>TenantConnectionInterceptor</c> remarks), or a malformed/empty-GUID claim value.
/// Callers should treat <c>null</c> the same way the old per-controller helpers did:
/// <c>return Forbid();</c>. This mirrors <c>User.FindFirst("tenant_id")</c> read-only — it is
/// not an authorization check by itself.
///
/// NOT the same responsibility as <see cref="ITenantSessionOverride"/>: this resolves whose OWN
/// tenant the current staff request belongs to, straight from the JWT. That interface instead
/// lets an already-trusted operation temporarily assume a DIFFERENT, explicitly-chosen tenant's
/// RLS context (the consumer/cross-tenant case, which structurally has no tenant_id claim at
/// all). Do not merge the two or use one in place of the other.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current request's tenant id, or <c>null</c> if none/invalid. Reads live off the
    /// current <c>HttpContext</c> on every access (no caching) — safe to call multiple times
    /// per request, always reflects the same JWT claim <c>TenantConnectionInterceptor</c> keys
    /// the DB session variables off.
    /// </summary>
    Guid? TenantId { get; }
}
