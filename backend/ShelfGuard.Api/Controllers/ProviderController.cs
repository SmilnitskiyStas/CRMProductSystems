using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Super Admin panel endpoints — provider role only.
/// Full implementation: TASK-020.
/// </summary>
[ApiController]
[Route("api/provider")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class ProviderController : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Health() =>
        Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
