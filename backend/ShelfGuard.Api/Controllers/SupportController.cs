using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Support;
using ShelfGuard.Application.Features.Support.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/support")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class SupportController(ISupportService supportService) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets([FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();
        var tickets = await supportService.GetTenantTicketsAsync(tenantId.Value, status, ct);
        return Ok(tickets);
    }

    [HttpGet("tickets/{id:guid}")]
    public async Task<IActionResult> GetTicket(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();
        var ticket = await supportService.GetTicketAsync(id, tenantId.Value, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();
        if (tenantId is null || userId is null) return Forbid();
        var ticket = await supportService.CreateTicketAsync(tenantId.Value, userId.Value, req, ct);
        return Created($"/api/support/tickets/{ticket.Id}", ticket);
    }

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> AddMessage(Guid id, [FromBody] AddMessageRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();
        if (tenantId is null || userId is null) return Forbid();
        try
        {
            var msg = await supportService.AddClientMessageAsync(id, tenantId.Value, userId.Value, req, ct);
            return Ok(msg);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private Guid? GetTenantId()
    {
        var raw = User.FindFirstValue("tenant_id");
        return Guid.TryParse(raw, out var g) ? g : null;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}
