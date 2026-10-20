using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// Singleton: runs the check in its own DI scope + tenant-scoped transaction (the JwtBearer event
/// fires before the request is authenticated, so the ambient connection has no tenant context).
/// Positive results are cached 30 s, negative 5 min (a closed session never reopens).
/// </summary>
public sealed class OperatorSessionValidator : IOperatorSessionValidator, IOperatorSessionCache
{
    public static readonly TimeSpan PositiveTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;

    public OperatorSessionValidator(IServiceScopeFactory scopes, IMemoryCache cache)
    {
        _scopes = scopes;
        _cache = cache;
    }

    private static string Key(Guid sessionId) => $"opsess:{sessionId:N}";

    public void Invalidate(Guid sessionId) => _cache.Remove(Key(sessionId));

    public async Task<bool> IsValidAsync(
        Guid tenantId, Guid sessionId, Guid registerId, Guid userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key(sessionId), out bool cached))
            return cached;

        bool valid;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantSessionOverride>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            valid = await tenantScope.ExecuteAsync(tenantId, async () =>
            {
                var row = await db.RegisterSessions.AsNoTracking()
                    .Where(s => s.Id == sessionId && s.TenantId == tenantId && s.RegisterId == registerId
                                && s.CashierId == userId && s.EndedAt == null)
                    .Select(s => new
                    {
                        RegisterStatus = s.Register!.Status,
                        UserActive = s.Cashier!.IsActive,
                        UserRole = s.Cashier.Role,
                    })
                    .FirstOrDefaultAsync(ct);
                if (row is null
                    || row.RegisterStatus != Domain.Entities.CashRegister.StatusActive
                    || !row.UserActive
                    || !AppPolicies.CanAccessPosRoles.Contains(row.UserRole))
                    return false;

                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
                return tenant is { IsActive: true } && tenant.HasModule("pos");
            }, ct);
        }

        _cache.Set(Key(sessionId), valid, valid ? PositiveTtl : NegativeTtl);
        return valid;
    }
}
