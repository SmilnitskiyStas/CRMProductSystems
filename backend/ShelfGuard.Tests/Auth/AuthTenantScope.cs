using ShelfGuard.Application.Services;

namespace ShelfGuard.Tests.Auth;

// Unit fixtures execute delegates; PostgreSQL acceptance separately verifies SET LOCAL/RLS.
internal sealed class AuthTenantScope : ITenantSessionOverride
{
    public Guid? TenantId { get; private set; }
    public async Task<T> ExecuteAsync<T>(Guid tenantId, Func<Task<T>> action, CancellationToken ct = default)
    {
        if (TenantId != null) throw new InvalidOperationException("Nested auth tenant scope.");
        TenantId = tenantId;
        try { return await action(); }
        finally { TenantId = null; }
    }
}
