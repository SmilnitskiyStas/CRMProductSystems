using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/pos/sync/catalog-v2"), Authorize(Policy = AppPolicies.CanAccessPos), RequireModule("pos"), AllowOperatorToken]
public sealed class PosScopedCatalogController(IPosScopedCatalog catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? cursor, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenant) || tenant == Guid.Empty
            || !Guid.TryParse(User.FindFirstValue("register_id"), out var register) || register == Guid.Empty) return Forbid();
        if (cursor?.Length > 16000) return BadRequest(new { error = "Invalid catalog cursor." });
        try { return Ok(await catalog.ReadAsync(tenant, register, cursor, ct)); }
        catch (PosPricingAuthorityException e) { return Conflict(new { error = e.Code }); }
    }
}
