using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Support;
using ShelfGuard.Application.Features.Support.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/provider/support")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderSupportController(ISupportService supportService) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid?   assignedTo,
        CancellationToken ct)
    {
        var tickets = await supportService.GetAllTicketsAsync(status, assignedTo, ct);
        return Ok(tickets);
    }

    [HttpGet("tickets/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ticket = await supportService.GetTicketByIdAsync(id, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPut("tickets/{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTicketRequest req, CancellationToken ct)
    {
        try
        {
            await supportService.AssignTicketAsync(id, req.AgentId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPut("tickets/{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTicketStatusRequest req, CancellationToken ct)
    {
        try
        {
            await supportService.UpdateStatusAsync(id, req.Status, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPatch("tickets/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        try
        {
            await supportService.MarkReadByProviderAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> AddMessage(Guid id, [FromBody] AddMessageRequest req, CancellationToken ct)
    {
        var senderId = GetUserId();
        if (senderId is null) return Forbid();
        try
        {
            var msg = await supportService.AddProviderMessageAsync(id, senderId.Value, req, ct);
            return Ok(msg);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}
