using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig.BlockRegistry;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Serves the server-owned Block Registry (TASK-538, CLAUDE CODE SPEC §12) — the catalog of block
/// types (<c>displayName</c>/<c>icon</c>/<c>category</c>/<c>defaultProps</c>/<c>validationSchema</c>/
/// <c>supportedDataSource</c>) backing the App Builder. TASK-539's drag & drop canvas and TASK-540's
/// Block Property Editor both fetch this instead of carrying their own hardcoded copy of block
/// metadata.
///
/// Not tenant-scoped — the catalog is identical, compile-time-fixed data for every tenant (no
/// retailer ever creates a new block *type*, only arranges instances of these fixed types on their
/// pages, TASK-539/541) — so this needs no <c>ITenantContext</c>, only an authenticated Retailer
/// Admin session. Same <c>AtLeastEnterpriseAdmin</c> / versioned-<c>/api/v1/</c> admin-surface
/// posture as <see cref="MobileThemeController"/> (decision 2, TASK-556) — deliberately a separate,
/// authenticated controller from the anonymous consumer-facing <see cref="MobileConfigController"/>.
///
/// Generic serialization only: every response is built by mapping whatever
/// <see cref="IBlockRegistryProvider"/> returns through <see cref="BlockDefinitionDto.From"/> — no
/// per-block-type branching lives in this controller.
/// </summary>
[ApiController]
[Route("api/v1/mobile/blocks")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class MobileBlocksController : ControllerBase
{
    private readonly IBlockRegistryProvider _registry;

    public MobileBlocksController(IBlockRegistryProvider registry) => _registry = registry;

    /// <summary>Every registered block type, in catalog order.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BlockDefinitionDto>), StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        var dtos = _registry.GetAll().Select(BlockDefinitionDto.From).ToList();
        return Ok(dtos);
    }

    /// <summary>One block type's definition, or 404 if <paramref name="type"/> is not registered.</summary>
    [HttpGet("{type}")]
    [ProducesResponseType(typeof(BlockDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetByType(string type)
    {
        var def = _registry.TryGet(type);
        if (def is null)
            return NotFound(new { error = $"Unknown block type '{type}'." });

        return Ok(BlockDefinitionDto.From(def));
    }
}
