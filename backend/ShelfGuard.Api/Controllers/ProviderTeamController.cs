using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/provider/team")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderTeamController(
    IProviderTeamService teamService,
    IProviderStatsService statsService) : ControllerBase
{
    // TASK-363 (Block 12 audit): the caller's own role, read from the validated JWT — used by
    // the service layer to gate owner-role escalation/deactivation. Never trust a role from
    // the request body for this purpose.
    private string ActingRole => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

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
        var (member, error) = await teamService.InviteMemberAsync(req, ActingRole, ct);
        if (error is not null) return BadRequest(new { error });
        return Created($"/api/provider/team", member);
    }

    [HttpPut("{memberId:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Update(Guid memberId, [FromBody] UpdateProviderMemberRequest req, CancellationToken ct)
    {
        var (member, error) = await teamService.UpdateMemberAsync(memberId, req, ActingRole, ct);
        if (error is not null) return BadRequest(new { error });
        return Ok(member);
    }

    [HttpDelete("{memberId:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Deactivate(Guid memberId, CancellationToken ct)
    {
        var (success, error) = await teamService.DeactivateMemberAsync(memberId, ActingRole, ct);
        if (!success && error is not null && error != "Member not found.")
            return BadRequest(new { error });
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{memberId:guid}/reactivate")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Reactivate(Guid memberId, CancellationToken ct)
    {
        var (success, error) = await teamService.ReactivateMemberAsync(memberId, ActingRole, ct);
        if (!success && error is not null && error != "Member not found.")
            return BadRequest(new { error });
        return success ? NoContent() : NotFound();
    }
}
