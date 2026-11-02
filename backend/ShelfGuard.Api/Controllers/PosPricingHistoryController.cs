using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/erp/pricing/history"), Authorize(Policy = AppPolicies.CanViewStock), RequireModule("inventory")]
public sealed class PosPricingHistoryController(IPosPricingHistory history) : ControllerBase
{
    [HttpGet("sales/{id:guid}")]
    public async Task<IActionResult> Snapshot(Guid id, CancellationToken ct)
    {
        try { return Ok(await history.SnapshotAsync(id, ct)); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "pricing_archive_not_found" }); }
        catch (PricingConflict e) { return Conflict(new { code = e.Code, error = e.Message }); }
    }
    [HttpGet("channels")]
    public async Task<IActionResult> Channels(DateTimeOffset from, DateTimeOffset until, Guid? locationId, CancellationToken ct)
    {
        try { return Ok(await history.ChannelsAsync(from, until, locationId, ct)); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_archive_period" }); }
    }
    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(DateTimeOffset from, DateTimeOffset until, Guid? locationId, CancellationToken ct, int page = 1)
    {
        try { return Ok(await history.DiagnosticsAsync(from, until, locationId, page, ct)); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_archive_period" }); }
    }
}
