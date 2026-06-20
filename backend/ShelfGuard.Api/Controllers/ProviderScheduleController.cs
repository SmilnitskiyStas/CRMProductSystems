using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/provider/schedules")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderScheduleController(IProviderScheduleService scheduleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? userId, CancellationToken ct)
    {
        var slots = await scheduleService.GetAllAsync(userId, ct);
        return Ok(slots);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Create([FromBody] CreateProviderScheduleSlotRequest req, CancellationToken ct)
    {
        var (slot, error) = await scheduleService.CreateAsync(req, ct);
        if (error is not null) return BadRequest(new { error });
        return Created("/api/provider/schedules", slot);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await scheduleService.DeleteAsync(id, ct);
        return success ? NoContent() : NotFound();
    }
}
