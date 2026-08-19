using Microsoft.AspNetCore.Http;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// <see cref="IHttpContextAccessor"/>-backed implementation — see <see cref="ITenantContext"/>
/// for the contract. TASK-528. Scoped (not singleton): wraps the request-scoped
/// <c>HttpContext</c>, same lifetime discipline as <c>TenantSessionOverride</c>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public TenantContext(IHttpContextAccessor http) => _http = http;

    public Guid? TenantId
    {
        get
        {
            var raw = _http.HttpContext?.User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
        }
    }
}
