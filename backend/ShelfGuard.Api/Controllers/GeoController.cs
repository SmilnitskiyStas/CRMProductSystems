using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Geo;
using ShelfGuard.Application.Features.Geo.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Geo taxonomy — the Ukraine region registry (oblast-level units + major cities).
/// Public and cacheable: region pickers on the marketplace, supplier profile and location
/// forms all render from GET /api/geo/regions instead of hardcoding the list. Mirrors the
/// [AllowAnonymous] precedent set by the marketplace item-categories endpoint.
/// </summary>
[ApiController]
[Route("api/geo")]
public sealed class GeoController : ControllerBase
{
    private readonly IGeoService _geoService;

    public GeoController(IGeoService geoService)
    {
        _geoService = geoService;
    }

    /// <summary>
    /// All Ukraine regions: 27 ISO 3166-2:UA oblast-level units + 24 major cities.
    /// UA-30 = the city of Kyiv, UA-32 = Kyiv oblast.
    /// </summary>
    [HttpGet("regions")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<RegionDto>), StatusCodes.Status200OK)]
    public IActionResult GetRegions()
    {
        return Ok(_geoService.GetRegions());
    }
}
