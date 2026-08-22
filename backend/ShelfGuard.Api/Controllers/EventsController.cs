using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Events;
using ShelfGuard.Application.Features.Events.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/events")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class EventsController : ControllerBase
{
    private readonly IEventService _events;

    public EventsController(IEventService events) => _events = events;

    /// <summary>Lists demand events in range, optionally filtered to any of the given stores
    /// (network-scoped events always included; "stores"-scoped events match on any overlap).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DemandEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? storeIds,
        CancellationToken ct)
        => Ok(await _events.GetAsync(from, to, storeIds, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DemandEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var (ev, error) = await _events.GetByIdAsync(id, ct);
        return error is not null ? NotFound(new { error }) : Ok(ev);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DemandEventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(UpsertEventRequest request, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null) return Forbid();

        var (ev, error) = await _events.CreateAsync(tenantId.Value, userId, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Created($"/api/events/{ev!.Id}", ev);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(DemandEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpsertEventRequest request, CancellationToken ct)
    {
        var (ev, error) = await _events.UpdateAsync(id, request, ct);
        if (error == "Event not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(ev);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var error = await _events.DeleteAsync(id, ct);
        return error is not null ? NotFound(new { error }) : NoContent();
    }

    [HttpGet("{id:guid}/coefficients")]
    [ProducesResponseType(typeof(List<EventCoefficientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCoefficients(Guid id, CancellationToken ct)
    {
        var (ev, error) = await _events.GetByIdAsync(id, ct);
        return error is not null ? NotFound(new { error }) : Ok(ev!.Coefficients);
    }

    [HttpPost("{id:guid}/coefficients")]
    [ProducesResponseType(typeof(EventCoefficientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddCoefficient(Guid id, CreateCoefficientRequest request, CancellationToken ct)
    {
        var (coef, error) = await _events.AddCoefficientAsync(id, request, ct);
        if (error == "Event not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Created($"/api/events/{id}/coefficients/{coef!.Id}", coef);
    }

    [HttpPut("{id:guid}/coefficients/{coefId:guid}")]
    [ProducesResponseType(typeof(EventCoefficientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCoefficient(
        Guid id, Guid coefId, UpdateCoefficientRequest request, CancellationToken ct)
    {
        var (coef, error) = await _events.UpdateCoefficientAsync(id, coefId, request, ct);
        if (error == "Coefficient not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(coef);
    }

    [HttpDelete("{id:guid}/coefficients/{coefId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveCoefficient(Guid id, Guid coefId, CancellationToken ct)
    {
        var error = await _events.RemoveCoefficientAsync(id, coefId, ct);
        return error is not null ? NotFound(new { error }) : NoContent();
    }

    [HttpPost("seed-defaults")]
    [ProducesResponseType(typeof(SeedDefaultsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SeedDefaults(CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var (result, error) = await _events.SeedDefaultsAsync(tenantId.Value, ct);
        return error is not null ? BadRequest(new { error }) : Ok(result);
    }

    private (Guid? TenantId, Guid? UserId) GetContext()
    {
        Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId);
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return (tenantId == Guid.Empty ? null : tenantId, userId == Guid.Empty ? null : userId);
    }
}
