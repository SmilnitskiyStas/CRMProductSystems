using ShelfGuard.Application.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Store -> production requests. Store-side actions (create/edit/submit/cancel) need no module
/// (a store need not run the production module itself); production-side actions
/// (review/reject/summary/ship) require the "production" module.
/// Thin controller: delegates to IProductionRequestService; row visibility is enforced by RLS.
/// </summary>
[ApiController]
[Route("api/production-requests")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class ProductionRequestsController : ControllerBase
{
    private readonly IProductionRequestService _svc;
    private readonly IProductionDemandService _demand;
    private readonly IWorkAccessContext _access;

    public ProductionRequestsController(IProductionRequestService svc, IProductionDemandService demand, IWorkAccessContext access)
    {
        _access = access;
        _svc = svc;
        _demand = demand;
    }

    // ── Shared reads ──────────────────────────────────────────────────────────

    /// <param name="status">Comma-separated statuses (draft,submitted,accepted,partial,rejected,fulfilled,cancelled).</param>
    [HttpGet]
    [ProducesResponseType(typeof(List<ProductionRequestListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? storeLocationId,
        [FromQuery] Guid? productionLocationId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? neededFrom,
        [FromQuery] DateOnly? neededTo,
        CancellationToken ct) =>
        Ok(await _svc.ListAsync(storeLocationId, productionLocationId, status, neededFrom, neededTo, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // ── Store side ────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ProductionRequestCreateDto dto, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var scope = await _access.GetAsync(ct);
        if (scope == null || !scope.CanLocation(dto.StoreLocationId)) return Forbid();
        var (request, error, code) = await _svc.CreateAsync(dto, tenantId.Value, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : StatusCode(StatusCodes.Status201Created, request);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductionRequestUpdateDto dto, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: false, ct)) return Forbid();
        var (request, error, code) = await _svc.UpdateAsync(id, dto, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: false, ct)) return Forbid();
        var (request, error, code) = await _svc.SubmitAsync(id, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: false, ct)) return Forbid();
        var (request, error, code) = await _svc.CancelAsync(id, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    /// <summary>
    /// Demand analysis for self-produced items: creates/refreshes one auto DRAFT request per store
    /// (never submitted automatically). Returns the created/changed drafts; each line carries an
    /// <c>autoReason</c>. 422 when the tenant has no active production location.
    /// </summary>
    [HttpPost("auto-generate")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [ProducesResponseType(typeof(List<ProductionRequestDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AutoGenerate([FromBody] ProductionAutoGenerateDto? dto, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null) return Forbid();

        var scope = await _access.GetAsync(ct);
        if (scope == null || (!scope.TenantWide && (dto?.StoreLocationId is not Guid store || !scope.CanLocation(store)))) return Forbid();
        var (drafts, error, code) = await _demand.GenerateAsync(tenantId.Value, userId, dto?.StoreLocationId, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(drafts);
    }

    // ── Production side ───────────────────────────────────────────────────────

    /// <summary>Per-item aggregate of open requests (default statuses: submitted, accepted, partial).</summary>
    [HttpGet("summary")]
    [Authorize(Policy = AppPolicies.ProductionView)]
    [RequireModule("production")]
    [ProducesResponseType(typeof(List<ProductionRequestSummaryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        [FromQuery] Guid? productionLocationId, [FromQuery] string? status, CancellationToken ct)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null || (!scope.TenantWide && (productionLocationId is not Guid location || !scope.CanLocation(location)))) return Forbid();
        return Ok(await _svc.GetSummaryAsync(productionLocationId, status, ct));
    }

    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [RequireRecentMfa]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(Guid id, [FromBody] ProductionRequestReviewDto dto, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: true, ct)) return Forbid();
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (request, error, code) = await _svc.ReviewAsync(id, dto, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [RequireRecentMfa]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ProductionRequestRejectDto dto, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: true, ct)) return Forbid();
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (request, error, code) = await _svc.RejectAsync(id, dto.Reason, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    /// <summary>
    /// Re-syncs the request's Planned production orders with its accepted quantities (idempotent).
    /// Re-creates orders for lines left unassigned (e.g. after an order was cancelled).
    /// </summary>
    [HttpPost("{id:guid}/sync-orders")]
    [Authorize(Policy = AppPolicies.ProductionManage)]
    [RequireRecentMfa]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncOrders(Guid id, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: true, ct)) return Forbid();
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (request, error, code) = await _svc.SyncOrdersAsync(id, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = AppPolicies.ProductionComplete)]
    [RequireRecentMfa]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ship(Guid id, CancellationToken ct)
    {
        if (!await CanAccessRequestLocationAsync(id, productionSide: true, ct)) return Forbid();
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (request, error, code) = await _svc.ShipAsync(id, tenantId.Value, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    private async Task<bool> CanAccessRequestLocationAsync(Guid id, bool productionSide, CancellationToken ct)
    {
        var scope = await _access.GetAsync(ct);
        if (scope == null) return false;
        var request = await _svc.GetByIdAsync(id, ct);
        return request != null && scope.CanLocation(productionSide ? request.ProductionLocationId : request.StoreLocationId);
    }

    private (Guid? TenantId, Guid? UserId) GetContext()
    {
        var tenant = User.FindFirstValue("tenant_id");
        var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return (
            Guid.TryParse(tenant, out var t) && t != Guid.Empty ? t : null,
            Guid.TryParse(user, out var u) && u != Guid.Empty ? u : null);
    }
}
