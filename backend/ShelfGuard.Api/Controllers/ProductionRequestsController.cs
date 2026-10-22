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

    public ProductionRequestsController(IProductionRequestService svc) => _svc = svc;

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
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ProductionRequestCreateDto dto, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (request, error, code) = await _svc.CreateAsync(dto, tenantId.Value, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : StatusCode(StatusCodes.Status201Created, request);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductionRequestUpdateDto dto, CancellationToken ct)
    {
        var (request, error, code) = await _svc.UpdateAsync(id, dto, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var (request, error, code) = await _svc.SubmitAsync(id, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var (request, error, code) = await _svc.CancelAsync(id, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    // ── Production side ───────────────────────────────────────────────────────

    /// <summary>Per-item aggregate of open requests (default statuses: submitted, accepted, partial).</summary>
    [HttpGet("summary")]
    [RequireModule("production")]
    [ProducesResponseType(typeof(List<ProductionRequestSummaryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        [FromQuery] Guid? productionLocationId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await _svc.GetSummaryAsync(productionLocationId, status, ct));

    [HttpPost("{id:guid}/review")]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(Guid id, [FromBody] ProductionRequestReviewDto dto, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (request, error, code) = await _svc.ReviewAsync(id, dto, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/reject")]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ProductionRequestRejectDto dto, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (request, error, code) = await _svc.RejectAsync(id, dto.Reason, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
    }

    [HttpPost("{id:guid}/ship")]
    [RequireModule("production")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductionRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ship(Guid id, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (request, error, code) = await _svc.ShipAsync(id, tenantId.Value, userId.Value, ct);
        return error is not null ? StatusCode(code!.Value, new { error }) : Ok(request);
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
