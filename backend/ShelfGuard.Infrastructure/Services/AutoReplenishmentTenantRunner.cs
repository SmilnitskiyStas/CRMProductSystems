using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// TASK-755 - see <see cref="IAutoReplenishmentTenantRunner"/>. Identical RLS contract to
/// <see cref="ProductionDemandTenantRunner"/>: all SET LOCAL inside one transaction
/// (app.tenant_id = tenantId, app.role = enterprise_admin = tenant-wide RESTRICTIVE store scope only,
/// null-uuid user/consumer ids), so it can only read and write that tenant's rows and nothing leaks
/// into a pooled connection. Deliberately NOT the worker/provider bypass.
/// </summary>
public sealed class AutoReplenishmentTenantRunner : IAutoReplenishmentTenantRunner
{
    private const string TenantWideRole = "enterprise_admin";

    private readonly AppDbContext _db;
    private readonly TenantContext _tenantContext;
    private readonly IAutoReplenishmentService _service;

    public AutoReplenishmentTenantRunner(AppDbContext db, TenantContext tenantContext, IAutoReplenishmentService service)
    {
        _db = db;
        _tenantContext = tenantContext;
        _service = service;
    }

    public async Task<AutoReplenishmentRunResult> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new(AutoReplenishmentRunStatus.TenantNotFound, [], "Tenant not found.");
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Auto replenishment run must start without an ambient transaction.");

        using var binding = _tenantContext.BindJobTenant(tenantId);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Guid :D (hex + dashes) and a constant role: no injection surface; SET LOCAL takes no bind params.
#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL app.tenant_id = '{tenantId:D}'; " +
            $"SET LOCAL app.role = '{TenantWideRole}'; " +
            "SET LOCAL app.user_id = '00000000-0000-0000-0000-000000000000'; " +
            "SET LOCAL app.consumer_account_id = '00000000-0000-0000-0000-000000000000';",
            ct);
#pragma warning restore EF1002

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
            return new(AutoReplenishmentRunStatus.TenantNotFound, [], "Tenant not found.");
        if (!tenant.IsActive)
            return new(AutoReplenishmentRunStatus.Skipped, [], "Tenant is inactive.");
        if (!await _db.Locations.AnyAsync(l => l.IsActive && l.Type != "production", ct))
            return new(AutoReplenishmentRunStatus.Skipped, [], "Tenant has no active non-production location.");

        var (drafts, error, _) = await _service.GenerateAsync(tenantId, null, ct);
        if (error is not null)
            return new(AutoReplenishmentRunStatus.Skipped, [], error); // tx disposed -> rollback

        await tx.CommitAsync(ct);
        return new(AutoReplenishmentRunStatus.Completed, drafts ?? []);
    }
}
