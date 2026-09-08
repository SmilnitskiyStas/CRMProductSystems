using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.ConsumerAssistant;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Consumer-facing AI shopping assistant (managed-AI Phase 4b). Requires a ConsumerAccount
/// session JWT (claim <c>consumer_account_id</c>), never a staff token. The assistant only
/// ever sees the calling consumer's own loyalty data for <c>{tenantId}</c> plus that tenant's
/// public catalog / program rules / stores, and refuses anything else (hardened
/// <c>AiSlot.Consumer</c> guardrail). Available only for a tenant whose provider has configured
/// and enabled the <c>ai_consumer</c> slot — otherwise 404.
/// </summary>
[ApiController]
[Route("api/consumer/assistant")]
[Authorize]
public sealed class ConsumerAssistantController : ControllerBase
{
    private readonly IConsumerAssistantService _assistant;

    public ConsumerAssistantController(IConsumerAssistantService assistant) => _assistant = assistant;

    /// <summary>Ask the tenant's consumer assistant a question.</summary>
    [HttpPost("{tenantId:guid}/ask")]
    [EnableRateLimiting("consumer-ai")]
    [ProducesResponseType(typeof(ConsumerAssistantAnswer), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ask(Guid tenantId, [FromBody] ConsumerAssistantAskRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (answer, error, statusCode) = await _assistant.AskAsync(consumerId.Value, tenantId, request.Message ?? string.Empty, ct);
        return error is null
            ? Ok(answer)
            : StatusCode(statusCode ?? 400, new { error });
    }

    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
