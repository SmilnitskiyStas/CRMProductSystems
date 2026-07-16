using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.ServiceDesk;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Provider cross-tenant Service Desk.
/// All endpoints require ProviderOnly policy — no tenant scoping.
/// </summary>
[ApiController]
[Route("api/admin/service-desk")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class AdminServiceDeskController : ControllerBase
{
    private readonly IProviderTicketService _tickets;

    public AdminServiceDeskController(IProviderTicketService tickets) => _tickets = tickets;

    // ── GET /api/admin/service-desk ──────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(List<ProviderTicketListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid? tenantId,
        CancellationToken ct)
    {
        var result = await _tickets.GetAllAsync(status, tenantId, ct);
        return Ok(result);
    }

    // ── POST /api/admin/service-desk ─────────────────────────────────────────

    [HttpPost]
    [ProducesResponseType(typeof(ProviderTicketListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateProviderTicketDto dto,
        CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (ticket, error) = await _tickets.CreateAsync(userId.Value, dto, ct);
        if (error is not null) return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, ticket);
    }

    // ── GET /api/admin/service-desk/{id} ─────────────────────────────────────

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProviderTicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ticket = await _tickets.GetByIdAsync(id, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    // ── POST /api/admin/service-desk/{id}/comments ───────────────────────────

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(TicketCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid id, AddCommentDto dto, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (comment, error) = await _tickets.AddCommentAsync(id, userId.Value, dto, ct);
        if (error == "Ticket not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, comment);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
