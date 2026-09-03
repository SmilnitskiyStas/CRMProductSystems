using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier cabinet — demand analytics over the supplier's own marketplace order history
/// (supplier-portal expansion #7, Phase 6b). Read-only; no cross-buyer data leakage.
///
/// Gated at the class by the <c>marketplace_supplier</c> module (every supplier tenant has it);
/// the single action additionally requires the <c>analytics_view</c> supplier permission.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("marketplace_supplier")]
public sealed class SupplierCabinetAnalyticsController : ControllerBase
{
    private const int DefaultWindowDays = 30;

    private readonly ISupplierAnalyticsService _analytics;

    public SupplierCabinetAnalyticsController(ISupplierAnalyticsService analytics) =>
        _analytics = analytics;

    /// <summary>
    /// Demand analytics for the given window. <c>from</c>/<c>to</c> are <c>YYYY-MM-DD</c>; both
    /// omitted → the last 30 days. The range is capped at 366 days server-side (a wider request is
    /// clamped and the effective window echoed on the response).
    /// </summary>
    [HttpGet("analytics")]
    [ProducesResponseType(typeof(SupplierAnalyticsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnalytics(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.AnalyticsView))
            return Forbid();

        var resolvedTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedFrom = from ?? resolvedTo.AddDays(-(DefaultWindowDays - 1));

        var result = await _analytics.GetAsync(tenantId.Value, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
