using Microsoft.AspNetCore.Http;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// <see cref="IHttpContextAccessor"/>-backed implementation — see <see cref="ITenantContext"/>
/// for the contract. TASK-528. Scoped (not singleton): wraps the request-scoped
/// <c>HttpContext</c>, same lifetime discipline as <c>TenantSessionOverride</c>.
///
/// TASK-754: an <c>internal</c> job-tenant binding lets ONE server-side job run
/// (<see cref="ProductionDemandTenantRunner"/>) make Application services that read
/// <see cref="ITenantContext"/> (e.g. OrderCalcService) see the tenant the job is bound to, on a
/// request that by construction carries no staff JWT. It is never a way to switch tenants on a
/// staff request: binding throws when the request already has a tenant claim, and the claim
/// always wins. Not reachable from outside this assembly (+ tests).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    private Guid? _jobTenant;

    public TenantContext(IHttpContextAccessor http) => _http = http;

    public Guid? TenantId => ClaimTenantId ?? _jobTenant;

    private Guid? ClaimTenantId
    {
        get
        {
            var raw = _http.HttpContext?.User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
        }
    }

    /// <summary>
    /// Binds this scope to <paramref name="tenantId"/> for the lifetime of the returned handle.
    /// Throws when the request is an authenticated staff request (has a tenant claim) or when a
    /// job tenant is already bound — one scope, one tenant.
    /// </summary>
    internal IDisposable BindJobTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Job tenant must be a real tenant id.", nameof(tenantId));
        if (ClaimTenantId is not null)
            throw new InvalidOperationException("Cannot bind a job tenant on a request that already carries a tenant claim.");
        if (_jobTenant is not null)
            throw new InvalidOperationException("A job tenant is already bound to this scope.");

        _jobTenant = tenantId;
        return new Binding(this);
    }

    private sealed class Binding(TenantContext owner) : IDisposable
    {
        private TenantContext? _owner = owner;

        public void Dispose()
        {
            if (_owner is null) return;
            _owner._jobTenant = null;
            _owner = null;
        }
    }
}
