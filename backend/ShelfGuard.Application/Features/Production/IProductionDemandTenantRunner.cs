using ShelfGuard.Application.Features.Production.Dtos;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// TASK-754: runs the automatic production-demand analysis (<see cref="IProductionDemandService"/>)
/// for exactly ONE tenant from the daily worker cron, on a request that carries no staff JWT.
///
/// Isolation contract: the whole run executes inside one DB transaction whose RLS context is
/// bound to <c>tenantId</c> only (SET LOCAL app.tenant_id + tenant-wide, non-bypass store scope) —
/// never the worker/provider bypass — so it can only read and write that tenant's rows. The
/// tenant is verified to exist, be active and have the <c>production</c> module before anything
/// runs. This is the ONLY operation this primitive performs; it is not a general "act as tenant"
/// escape hatch. The single caller is the worker-only internal endpoint.
/// </summary>
public interface IProductionDemandTenantRunner
{
    Task<ProductionDemandRunResult> RunAsync(Guid tenantId, CancellationToken ct = default);
}

public enum ProductionDemandRunStatus
{
    /// <summary>Generation ran; <see cref="ProductionDemandRunResult.Drafts"/> lists created/changed drafts.</summary>
    Completed,
    /// <summary>No tenant with that id.</summary>
    TenantNotFound,
    /// <summary>Tenant inactive, without the production module, or without an active production location.</summary>
    Skipped,
}

public sealed record ProductionDemandRunResult(
    ProductionDemandRunStatus Status,
    List<ProductionRequestDetailDto> Drafts,
    string? Reason = null);
