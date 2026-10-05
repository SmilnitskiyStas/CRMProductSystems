using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Application.Features.Leads.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Provider-team view over leads captured from the public landing forms (TASK-721).
/// Same ProviderTeamMember policy as client management (view_clients/manage_clients are
/// enforced in the UI only; the backend gates provider endpoints by role).
/// </summary>
[ApiController]
[Route("api/provider/leads")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderLeadsController(ILandingLeadService leads) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(LandingLeadListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await leads.ListAsync(status, search, page, pageSize, ct));

    [HttpGet("count")]
    [ProducesResponseType(typeof(LeadCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Count(CancellationToken ct) =>
        Ok(await leads.CountUnprocessedAsync(ct));

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(LandingLeadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeadRequest request, CancellationToken ct)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid? userId = Guid.TryParse(claim, out var uid) ? uid : null;
        var (lead, error) = await leads.UpdateAsync(id, request, userId, ct);
        if (error is null) return Ok(lead);
        return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(new { error })
            : BadRequest(new { error });
    }
}
