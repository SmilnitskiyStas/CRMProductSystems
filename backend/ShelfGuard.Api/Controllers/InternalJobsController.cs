using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Features.Production;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// TASK-754: worker-only internal job endpoints. No user JWT — authenticated by
/// <see cref="InternalJobKeyFilter"/> (shared secret; disabled when unset) and kept off the public
/// gateway (/api/internal/ returns 404 there). Classified as CORE by RoleControllerFeatureProvider,
/// so it exists only on api_core.
///
/// Exactly one operation: run the production-demand analysis for ONE tenant, inside an RLS
/// context bound to that tenant only (see <see cref="IProductionDemandTenantRunner"/>).
/// The manual store-manager endpoint POST /api/production-requests/auto-generate is unchanged.
/// </summary>
[ApiController]
[Route("api/internal")]
[AllowAnonymous]
[TypeFilter(typeof(InternalJobKeyFilter))]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalJobsController : ControllerBase
{
    private readonly IProductionDemandTenantRunner _runner;

    public InternalJobsController(IProductionDemandTenantRunner runner) => _runner = runner;

    public sealed record ProductionDemandRunRequest(Guid TenantId);

    public sealed record ProductionDemandRunDraft(Guid Id, string StoreLocationName, string Source, int LineCount);

    public sealed record ProductionDemandRunResponse(Guid TenantId, string Status, string? Reason, List<ProductionDemandRunDraft> Drafts);

    /// <summary>
    /// 200 completed (created/changed drafts) · 404 unknown tenant · 422 skipped (inactive tenant,
    /// no production module, or no active production location).
    /// </summary>
    [HttpPost("production-demand/run")]
    [ProducesResponseType(typeof(ProductionDemandRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProductionDemandRunResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RunProductionDemand([FromBody] ProductionDemandRunRequest? dto, CancellationToken ct)
    {
        if (dto is null || dto.TenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var result = await _runner.RunAsync(dto.TenantId, ct);
        var body = new ProductionDemandRunResponse(
            dto.TenantId,
            result.Status switch
            {
                ProductionDemandRunStatus.Completed => "completed",
                ProductionDemandRunStatus.Skipped => "skipped",
                _ => "not_found",
            },
            result.Reason,
            result.Drafts.Select(d => new ProductionDemandRunDraft(d.Id, d.StoreLocationName, d.Source, d.Lines.Count)).ToList());

        return result.Status switch
        {
            ProductionDemandRunStatus.Completed => Ok(body),
            ProductionDemandRunStatus.TenantNotFound => NotFound(new { error = result.Reason ?? "Tenant not found." }),
            _ => UnprocessableEntity(body),
        };
    }
}
