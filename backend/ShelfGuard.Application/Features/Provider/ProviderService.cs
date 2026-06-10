using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

/// <summary>
/// Business logic for the Provider (super admin) panel.
/// All operations require role = "provider"; authorization is enforced at the controller level.
/// </summary>
public sealed class ProviderService : IProviderService
{
    private readonly ITenantRepository    _tenants;
    private readonly IActivityLogRepository _logs;
    private readonly IJwtService          _jwt;

    public ProviderService(
        ITenantRepository tenants,
        IActivityLogRepository logs,
        IJwtService jwt)
    {
        _tenants = tenants;
        _logs    = logs;
        _jwt     = jwt;
    }

    // ── Tenant listing ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken ct)
    {
        var tenants      = await _tenants.GetAllAsync(ct);
        var userCounts   = await _tenants.GetUserCountsAsync(ct);
        var storeCounts  = await _tenants.GetStoreCountsAsync(ct);
        var expiredCounts = await _tenants.GetExpiredBatchCountsAsync(ct);

        return tenants.Select(t => new TenantSummaryDto(
            t.Id,
            t.Name,
            t.Slug,
            t.Plan,
            ParseModules(t.Modules),
            t.IsActive,
            t.CreatedAt,
            userCounts.GetValueOrDefault(t.Id),
            storeCounts.GetValueOrDefault(t.Id),
            expiredCounts.GetValueOrDefault(t.Id)
        )).ToList();
    }

    public async Task<(TenantDetailDto? Tenant, string? Error)> GetTenantAsync(
        Guid tenantId, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, "Tenant not found.");

        var userCount    = (await _tenants.GetUserCountsAsync(ct)).GetValueOrDefault(tenantId);
        var storeCount   = (await _tenants.GetStoreCountsAsync(ct)).GetValueOrDefault(tenantId);
        var expiredCount = (await _tenants.GetExpiredBatchCountsAsync(ct)).GetValueOrDefault(tenantId);

        // Last activity = most recent log entry for this tenant
        var recentLogs = await _logs.GetByTenantAsync(tenantId, 1, ct);
        var lastActivity = recentLogs.FirstOrDefault()?.CreatedAt;

        return (new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Plan,
            ParseModules(tenant.Modules),
            tenant.IsActive,
            tenant.CreatedAt,
            userCount,
            storeCount,
            expiredCount,
            lastActivity), null);
    }

    // ── Plan & modules ──────────────────────────────────────────────────────

    public async Task<string?> UpdatePlanAsync(Guid tenantId, string plan, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return "Tenant not found.";

        var error = tenant.UpdatePlan(plan);
        if (error is not null) return error;

        await _tenants.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> UpdateModulesAsync(Guid tenantId, string[] modules, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return "Tenant not found.";

        var error = tenant.UpdateModules(modules);
        if (error is not null) return error;

        await _tenants.SaveChangesAsync(ct);
        return null;
    }

    // ── Impersonation ───────────────────────────────────────────────────────

    public async Task<(ImpersonateResponse? Response, string? Error)> ImpersonateAsync(
        Guid providerId, string providerEmail, Guid targetTenantId, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(targetTenantId, ct);
        if (tenant is null) return (null, "Tenant not found.");
        if (!tenant.IsActive)  return (null, "Tenant is deactivated.");

        // Generate a short-lived JWT scoped to the target tenant (enterprise_admin role).
        // This token includes an "impersonated=true" claim so audit logs can detect it.
        var token = _jwt.GenerateImpersonationToken(providerId, providerEmail, targetTenantId);

        // Log the impersonation event for audit trail.
        await _logs.LogAsync(new Domain.Entities.ActivityLog
        {
            Id            = Guid.NewGuid(),
            TenantId      = targetTenantId,
            UserId        = providerId,
            Action        = "provider.impersonate",
            EntityType    = "tenant",
            EntityId      = targetTenantId,
            Meta          = $"Provider {providerEmail} started impersonation of tenant '{tenant.Name}'",
            IsImpersonated = false,
            CreatedAt     = DateTime.UtcNow,
        }, ct);

        await _logs.SaveChangesAsync(ct);

        return (new ImpersonateResponse(token, tenant.Name, tenant.Id), null);
    }

    // ── Health & observability ──────────────────────────────────────────────

    public async Task<ProviderHealthDto> GetHealthAsync(CancellationToken ct)
    {
        var tenants        = await _tenants.GetAllAsync(ct);
        var totalUsers     = await _tenants.GetTotalUsersAsync(ct);
        var totalExpired   = await _tenants.GetTotalExpiredBatchesAsync(ct);

        return new ProviderHealthDto(
            TotalTenants:        tenants.Count,
            ActiveTenants:       tenants.Count(t => t.IsActive),
            TotalUsers:          totalUsers,
            TotalExpiredBatches: totalExpired,
            Timestamp:           DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<ProviderLogDto>> GetLogsAsync(int limit, CancellationToken ct)
    {
        var logs = await _logs.GetAllTenantsAsync(limit, ct);
        return logs.Select(l => new ProviderLogDto(
            l.Id,
            l.Action,
            l.EntityType ?? string.Empty,
            l.EntityId,
            l.Meta,
            l.IpAddress,
            l.UserId ?? Guid.Empty,
            l.TenantId,
            l.CreatedAt)).ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string[] ParseModules(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
