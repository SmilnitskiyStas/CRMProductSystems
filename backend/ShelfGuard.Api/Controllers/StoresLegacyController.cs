using Microsoft.AspNetCore.Mvc;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Legacy redirect controller. All /api/stores/* requests are permanently redirected
/// to /api/locations/*. Clients should update to use /api/locations directly.
/// </summary>
[ApiController]
[Route("api/stores")]
public sealed class StoresLegacyController : ControllerBase
{
    // Redirect GET /api/stores → /api/locations
    [HttpGet]
    public IActionResult RedirectList() =>
        RedirectPermanent("/api/locations");

    // Redirect GET /api/stores/{id} → /api/locations/{id}
    [HttpGet("{id}")]
    public IActionResult RedirectById(string id) =>
        RedirectPermanent($"/api/locations/{id}");

    // Redirect POST /api/stores → /api/locations
    [HttpPost]
    public IActionResult RedirectCreate() =>
        RedirectPermanent("/api/locations");

    // Redirect PUT /api/stores/{id} → /api/locations/{id}
    [HttpPut("{id}")]
    public IActionResult RedirectUpdate(string id) =>
        RedirectPermanent($"/api/locations/{id}");

    // Redirect PUT /api/stores/{id}/floor-plan → /api/locations/{id}/floor-plan
    [HttpPut("{id}/floor-plan")]
    public IActionResult RedirectFloorPlan(string id) =>
        RedirectPermanent($"/api/locations/{id}/floor-plan");

    // Redirect GET /api/stores/{id}/zones → /api/locations/{id}/zones
    [HttpGet("{id}/zones")]
    public IActionResult RedirectZones(string id) =>
        RedirectPermanent($"/api/locations/{id}/zones");

    // Redirect POST /api/stores/{id}/zones → /api/locations/{id}/zones
    [HttpPost("{id}/zones")]
    public IActionResult RedirectCreateZone(string id) =>
        RedirectPermanent($"/api/locations/{id}/zones");

    // Redirect PUT /api/stores/{id}/zones/{zoneId} → /api/locations/{id}/zones/{zoneId}
    [HttpPut("{id}/zones/{zoneId}")]
    public IActionResult RedirectUpdateZone(string id, string zoneId) =>
        RedirectPermanent($"/api/locations/{id}/zones/{zoneId}");

    // Redirect DELETE /api/stores/{id}/zones/{zoneId} → /api/locations/{id}/zones/{zoneId}
    [HttpDelete("{id}/zones/{zoneId}")]
    public IActionResult RedirectDeleteZone(string id, string zoneId) =>
        RedirectPermanent($"/api/locations/{id}/zones/{zoneId}");
}
