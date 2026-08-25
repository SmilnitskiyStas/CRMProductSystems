using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.CustomerSupport;
using ShelfGuard.Application.Features.CustomerSupport.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Staff-side inbox for consumer support tickets (TASK-616). Same authorization tier as
/// <see cref="CustomersController"/> (AtLeastStoreManager) — this is a customer-facing staff
/// surface (triaging and replying to shoppers), not an admin-only settings page. Deliberately
/// NOT gated by [RequireModule] — matches CustomersController's own unconditional access; the
/// consumer app's support channel isn't tied to a separately-activatable module the way loyalty
/// is. Consumer-facing counterpart is <see cref="ConsumerSupportController"/>.
/// </summary>
[ApiController]
[Route("api/customer-support")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class CustomerSupportInboxController : ControllerBase
{
    private readonly IConsumerSupportService _support;

    public CustomerSupportInboxController(IConsumerSupportService support) => _support = support;

    /// <summary>Inbox tickets for the calling tenant, newest first, optionally filtered by status, paged.</summary>
    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var tickets = await _support.GetInboxAsync(tenantId.Value, status, page, pageSize, ct);
        return Ok(tickets);
    }

    /// <summary>A single ticket with its messages (oldest first). Marks unread consumer messages read.</summary>
    [HttpGet("tickets/{id:guid}")]
    [ProducesResponseType(typeof(ConsumerSupportTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (ticket, error, statusCode) = await _support.GetTicketForStaffAsync(tenantId.Value, id, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(ticket);
    }

    /// <summary>Staff reply; bumps the ticket's UpdatedAt.</summary>
    [HttpPost("tickets/{id:guid}/reply")]
    [ProducesResponseType(typeof(ConsumerSupportTicketMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reply(
        Guid id, [FromBody] AddStaffSupportReplyRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (message, error, statusCode) = await _support.AddStaffReplyAsync(
            tenantId.Value, id, userId.Value, request.Body, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return StatusCode(StatusCodes.Status201Created, message);
    }

    /// <summary>Changes a ticket status (open | in_progress | resolved | closed).</summary>
    [HttpPut("tickets/{id:guid}/status")]
    [ProducesResponseType(typeof(ConsumerSupportTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateConsumerSupportTicketStatusRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (ticket, error, statusCode) = await _support.UpdateStatusAsync(
            tenantId.Value, id, userId.Value, request.Status, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(ticket);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
