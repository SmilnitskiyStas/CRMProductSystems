using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/provider/roles")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class ProviderRolesController(IProviderRolesService rolesService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var roles = await rolesService.GetAllAsync(ct);
        return Ok(roles);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Create([FromBody] CreateProviderRoleRequest req, CancellationToken ct)
    {
        var (role, error) = await rolesService.CreateAsync(req, ct);
        if (error is not null) return BadRequest(new { error });
        return Created($"/api/provider/roles/{role!.Id}", role);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProviderRoleRequest req, CancellationToken ct)
    {
        var (role, error) = await rolesService.UpdateAsync(id, req, ct);
        if (error is not null) return BadRequest(new { error });
        return Ok(role);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.ProviderCanInvite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (success, error) = await rolesService.DeleteAsync(id, ct);
        if (!success) return BadRequest(new { error });
        return NoContent();
    }
}
