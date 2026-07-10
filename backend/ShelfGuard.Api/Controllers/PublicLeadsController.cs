using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Application.Features.Leads.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>Public (unauthenticated) lead capture for the landing page (TASK-333).</summary>
[ApiController]
[Route("api/public/leads")]
public sealed class PublicLeadsController : ControllerBase
{
    private readonly ILandingLeadService _leads;

    public PublicLeadsController(ILandingLeadService leads) => _leads = leads;

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("public-leads")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Capture([FromBody] CaptureLeadRequest request, CancellationToken ct)
    {
        var error = await _leads.CaptureAsync(request, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }
}
