using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.AiAssistant;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// POST /api/ai/assistant — natural-language business query answered by Claude with
/// aggregated cross-module context (stock, orders, sales, suppliers).
/// TASK-250 / v4 Phase 6.
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
[RequireModule("inventory")]
public sealed class AiAssistantController : ControllerBase
{
    private readonly IAiAssistantService _assistant;

    public AiAssistantController(IAiAssistantService assistant) => _assistant = assistant;

    /// <summary>
    /// Ask the AI business assistant a natural-language question.
    /// Claude receives aggregated context (critical stock, pending orders,
    /// last-7-days sales, active suppliers) and returns a recommendation.
    /// </summary>
    [HttpPost("assistant")]
    [ProducesResponseType(typeof(BusinessAssistantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Ask(
        [FromBody] BusinessAssistantRequest request,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (response, error) = await _assistant.AskAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(response);
    }

    private Guid? GetTenantId()
    {
        Guid.TryParse(User.FindFirstValue("tenant_id"), out var id);
        return id == Guid.Empty ? null : id;
    }
}
