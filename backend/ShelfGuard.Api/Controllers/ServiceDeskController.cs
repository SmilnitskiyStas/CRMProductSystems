using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ServiceDesk;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/service-desk")]
[Authorize]
public sealed class ServiceDeskController : ControllerBase
{
    private readonly ITicketService _tickets;

    public ServiceDeskController(ITicketService tickets) => _tickets = tickets;

    // ── GET /api/service-desk  (managers only) ───────────────────────────────

    [HttpGet]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(PagedResult<TicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? assignedTo,
        [FromQuery] Guid? createdBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var result = await _tickets.GetPagedAsync(
            tenantId.Value, status, priority, assignedTo, createdBy,
            page, pageSize, ct);

        return Ok(result);
    }

    // ── GET /api/service-desk/my ─────────────────────────────────────────────

    [HttpGet("my")]
    [ProducesResponseType(typeof(List<TicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMy(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var result = await _tickets.GetMyTicketsAsync(userId.Value, tenantId.Value, ct);
        return Ok(result);
    }

    // ── GET /api/service-desk/{id} ───────────────────────────────────────────

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var ticket = await _tickets.GetByIdAsync(
            id, tenantId.Value, userId.Value, IsManager(), ct);

        return ticket is null ? NotFound() : Ok(ticket);
    }

    // ── POST /api/service-desk ───────────────────────────────────────────────

    [HttpPost]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateTicketDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (ticket, error) = await _tickets.CreateAsync(tenantId.Value, userId.Value, dto, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = ticket!.Id }, ticket);
    }

    // ── PUT /api/service-desk/{id} ───────────────────────────────────────────

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateTicketDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (ticket, error) = await _tickets.UpdateAsync(
            id, tenantId.Value, userId.Value, IsManager(), dto, ct);

        if (error == "Ticket not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(ticket);
    }

    // ── POST /api/service-desk/{id}/comments ────────────────────────────────

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(TicketCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid id, AddCommentDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (comment, error) = await _tickets.AddCommentAsync(
            id, tenantId.Value, userId.Value, IsManager(), dto, ct);

        if (error == "Ticket not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, comment);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// Returns true for store_manager and above (matching AtLeastStoreManager policy roles).
    /// Used to gate manager-only operations inside the service.
    /// </summary>
    private bool IsManager()
    {
        var role = User.FindFirstValue(ClaimTypes.Role)
                ?? User.FindFirstValue("role")
                ?? string.Empty;

        return role is AppRoles.Provider
                    or AppRoles.ProviderAdmin
                    or AppRoles.ProviderAgent
                    or AppRoles.EnterpriseAdmin
                    or AppRoles.NetworkManager
                    or AppRoles.StoreManager;
    }
}
