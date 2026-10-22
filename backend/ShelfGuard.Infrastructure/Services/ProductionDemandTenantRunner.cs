using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// TASK-754 — see <see cref="IProductionDemandTenantRunner"/> for the contract.
///
/// RLS context of the run (all SET LOCAL, so it reverts the instant the transaction ends —
/// commit, rollback or exception — and can never leak into a pooled connection's next user):
///   app.tenant_id = tenantId                 → canonical tenant_isolation matches ONLY this tenant
///   app.role      = 'enterprise_admin'       → tenant-wide RESTRICTIVE store_scope (all stores of
///                                              the chain, same view as the manual endpoint for an
///                                              enterprise admin). 'enterprise_admin' appears ONLY in
///                                              RESTRICTIVE store_scope policies (never in a permissive
///                                              policy), so it narrows nothing cross-tenant and grants
///                                              no bypass. Deliberately NOT 'worker'/'provider'
///                                              (those ARE cross-tenant bypasses).
///   app.user_id / app.consumer_account_id = null-uuid → no identity-based policy can match.
/// The interceptor RESETs everything at connection-open for this (unauthenticated) request, and
/// tenant_isolation doubles as WITH CHECK, so even a buggy write with another TenantId is
/// rejected by Postgres rather than persisted.
/// </summary>
public sealed class ProductionDemandTenantRunner : IProductionDemandTenantRunner
{
    private const string ProductionModule = "production";
    private const string TenantWideRole = "enterprise_admin";

    private readonly AppDbContext _db;
    private readonly TenantContext _tenantContext;
    private readonly IProductionDemandService _demand;

    public ProductionDemandTenantRunner(AppDbContext db, TenantContext tenantContext, IProductionDemandService demand)
    {
        _db = db;
        _tenantContext = tenantContext;
        _demand = demand;
    }

    public async Task<ProductionDemandRunResult> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new(ProductionDemandRunStatus.TenantNotFound, [], "Tenant not found.");
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Production demand run must start without an ambient transaction.");

        // Application services that read ITenantContext (OrderCalcService) must see this tenant.
        // Throws on an authenticated staff request — the claim tenant can never be swapped.
        using var binding = _tenantContext.BindJobTenant(tenantId);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // tenantId is a Guid formatted :D (hex + dashes only) and the role is a constant — no
        // injection surface; SET LOCAL does not accept bind parameters (same as TenantSessionOverride).
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
            return new(ProductionDemandRunStatus.TenantNotFound, [], "Tenant not found.");
        if (!tenant.IsActive)
            return new(ProductionDemandRunStatus.Skipped, [], "Tenant is inactive.");
        if (!tenant.HasModule(ProductionModule))
            return new(ProductionDemandRunStatus.Skipped, [], "Tenant has no production module.");

        // userId = null: auto drafts have no human author (CreatedBy stays null).
        var (drafts, error, _) = await _demand.GenerateAsync(tenantId, null, null, ct);
        if (error is not null)
            return new(ProductionDemandRunStatus.Skipped, [], error); // tx disposed → rollback

        await tx.CommitAsync(ct);
        return new(ProductionDemandRunStatus.Completed, drafts ?? []);
    }
}
