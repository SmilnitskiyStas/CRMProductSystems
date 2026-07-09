using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.LegalEntities.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Legal entities (юридичні особи) management within a tenant (TASK-322).
/// Write operations require <see cref="AppPolicies.AtLeastEnterpriseAdmin"/> OR the
/// caller's <c>User.Permissions["legal_entities.manage"] == true</c> override —
/// see <see cref="LegalEntityAuthorization"/>.
/// </summary>
[ApiController]
[Route("api/legal-entities")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class LegalEntitiesController : ControllerBase
{
    private readonly ILegalEntityService _legalEntities;

    public LegalEntitiesController(ILegalEntityService legalEntities) => _legalEntities = legalEntities;

    [HttpGet]
    [ProducesResponseType(typeof(List<LegalEntityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var entities = await _legalEntities.GetAllAsync(tenantId.Value, ct);
        return Ok(entities);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (entity, error) = await _legalEntities.GetByIdAsync(tenantId.Value, id, ct);
        return entity is null ? NotFound(new { error }) : Ok(entity);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateLegalEntityRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        if (!LegalEntityAuthorization.CanManage(User))
            return Forbid();

        var (entity, error) = await _legalEntities.CreateAsync(tenantId.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = entity!.Id }, entity);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateLegalEntityRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        if (!LegalEntityAuthorization.CanManage(User))
            return Forbid();

        var (entity, error) = await _legalEntities.UpdateAsync(tenantId.Value, id, request, ct);

        if (error == "Legal entity not found.")
            return NotFound(new { error });

        if (error is not null)
            return BadRequest(new { error });

        return Ok(entity);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        if (!LegalEntityAuthorization.CanManage(User))
            return Forbid();

        var error = await _legalEntities.DeactivateAsync(tenantId.Value, id, ct);
        return error is null ? NoContent() : NotFound(new { error });
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
