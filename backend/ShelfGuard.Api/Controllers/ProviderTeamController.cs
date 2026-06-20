using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/provider/team")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderTeamController(
    IProviderTeamService teamService,
    IProviderStatsService statsService) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await statsService.GetTeamStatsAsync(ct);
        return Ok(stats);
    }

    [HttpGet]
    public async Task<IActionResult> GetTeam(CancellationToken ct)
    {
        var members = await teamService.GetTeamAsync(ct);
        return Ok(members);
    }

    [HttpPost("invite")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Invite([FromBody] InviteProviderMemberRequest req, CancellationToken ct)
    {
        var (member, error) = await teamService.InviteMemberAsync(req, ct);
        if (error is not null) return BadRequest(new { error });
        return Created($"/api/provider/team", member);
    }

    [HttpPut("{memberId:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Update(Guid memberId, [FromBody] UpdateProviderMemberRequest req, CancellationToken ct)
    {
        var (member, error) = await teamService.UpdateMemberAsync(memberId, req, ct);
        if (error is not null) return BadRequest(new { error });
        return Ok(member);
    }

    [HttpDelete("{memberId:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Deactivate(Guid memberId, CancellationToken ct)
    {
        var success = await teamService.DeactivateMemberAsync(memberId, ct);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{memberId:guid}/reactivate")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Reactivate(Guid memberId, CancellationToken ct)
    {
        var success = await teamService.ReactivateMemberAsync(memberId, ct);
        return success ? NoContent() : NotFound();
    }
}
