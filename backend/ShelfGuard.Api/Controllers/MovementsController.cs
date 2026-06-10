using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Application.Features.Movements.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/movements")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class MovementsController : ControllerBase
{
    private readonly IMovementService _movements;

    public MovementsController(IMovementService movements) => _movements = movements;

    /// <summary>
    /// Returns paginated stock movement history.
    /// Filters: product_id, store_id, type (receipt/transfer/write_off/adjustment), from, to.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MovementPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(
        [FromQuery] Guid?   product_id,
        [FromQuery] Guid?   store_id,
        [FromQuery] string? type,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page      = 1,
        [FromQuery] int page_size = 50,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Forbid();

        var result = await _movements.GetAsync(
            tenantId, product_id, store_id, type, from, to, page, page_size, ct);

        return Ok(result);
    }

    private Guid GetTenantId()
    {
        var raw = User.FindFirstValue("tenant_id");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
