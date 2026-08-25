using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.CustomerSupport;
using ShelfGuard.Application.Features.CustomerSupport.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Consumer-facing support ticket channel (TASK-616) — requires a ConsumerAccount session JWT
/// (claim "consumer_account_id"), never a staff token. Same authorization shape as
/// <see cref="ConsumerLoyaltyController"/>/<see cref="ConsumerProfileController"/>: the claim is
/// the whole app-level boundary (belt-and-suspenders alongside consumer_support_tickets' own
/// consumer_self_access RLS policy). Staff-side counterpart is
/// <see cref="CustomerSupportInboxController"/>.
/// </summary>
[ApiController]
[Route("api/consumer/support")]
[Authorize]
public sealed class ConsumerSupportController : ControllerBase
{
    private readonly IConsumerSupportService _support;

    public ConsumerSupportController(IConsumerSupportService support) => _support = support;

    [HttpPost("tickets")]
    [ProducesResponseType(typeof(ConsumerSupportTicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTicket(
        [FromBody] CreateConsumerSupportTicketRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (ticket, error, statusCode) = await _support.CreateTicketAsync(
            consumerId.Value, request.TenantId, request.Subject, request.Body, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return StatusCode(StatusCodes.Status201Created, ticket);
    }

    /// <summary><paramref name="tenantId"/> is required — a consumer's tickets live per-tenant
    /// (mirrors <see cref="CreateTicket"/> taking TenantId in its body), unlike e.g.
    /// ConsumerLoyaltyController's cross-tenant GetMemberships.</summary>
    [HttpGet("tickets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] Guid tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var tickets = await _support.GetMyTicketsAsync(consumerId.Value, tenantId, page, pageSize, ct);
        return Ok(tickets);
    }

    [HttpGet("tickets/{id:guid}")]
    [ProducesResponseType(typeof(ConsumerSupportTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(Guid id, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (ticket, error, statusCode) = await _support.GetTicketAsync(consumerId.Value, id, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(ticket);
    }

    [HttpPost("tickets/{id:guid}/messages")]
    [ProducesResponseType(typeof(ConsumerSupportTicketMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMessage(
        Guid id, [FromBody] AddConsumerSupportTicketMessageRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (message, error, statusCode) = await _support.AddConsumerMessageAsync(
            consumerId.Value, id, request.Body, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return StatusCode(StatusCodes.Status201Created, message);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Mirrors ConsumerLoyaltyController.ResolveConsumerAccountId exactly — a staff JWT
    /// never carries this claim.</summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
