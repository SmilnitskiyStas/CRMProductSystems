using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Adu;
using ShelfGuard.Application.Features.Adu.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/adu")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class AduController : ControllerBase
{
    private readonly IAduService _adu;

    public AduController(IAduService adu) => _adu = adu;

    [HttpGet("{storeId:guid}/{productId:guid}")]
    [ProducesResponseType(typeof(AduDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid storeId, Guid productId, CancellationToken ct)
    {
        var (adu, error) = await _adu.GetAsync(storeId, productId, ct);
        return error is not null ? NotFound(new { error }) : Ok(adu);
    }

    [HttpPost("recalculate")]
    [ProducesResponseType(typeof(RecalculateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recalculate(RecalculateRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _adu.RecalculateAsync(tenantId.Value, request.StoreId, ct);
        if (error is not null)
            return NotFound(new { error });

        return Ok(result);
    }

    private Guid? GetTenantId()
    {
        var tenantIdStr = User.FindFirstValue("tenant_id");
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }
}
