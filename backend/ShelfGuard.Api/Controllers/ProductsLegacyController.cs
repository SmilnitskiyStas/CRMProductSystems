using Microsoft.AspNetCore.Mvc;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Legacy redirect controller. All /api/products/* requests are permanently redirected
/// to /api/items/*. Clients should update to use /api/items directly.
/// </summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsLegacyController : ControllerBase
{
    // Redirect GET /api/products → /api/items
    [HttpGet]
    public IActionResult RedirectList() =>
        RedirectPermanent("/api/items");

    // Redirect GET /api/products/{id} → /api/items/{id}
    [HttpGet("{id}")]
    public IActionResult RedirectById(string id) =>
        RedirectPermanent($"/api/items/{id}");

    // Redirect GET /api/products/by-barcode/{code} → /api/items/by-barcode/{code}
    [HttpGet("by-barcode/{code}")]
    public IActionResult RedirectByBarcode(string code) =>
        RedirectPermanent($"/api/items/by-barcode/{code}");

    // Redirect POST /api/products → /api/items
    [HttpPost]
    public IActionResult RedirectCreate() =>
        RedirectPermanent("/api/items");

    // Redirect PUT /api/products/{id} → /api/items/{id}
    [HttpPut("{id}")]
    public IActionResult RedirectUpdate(string id) =>
        RedirectPermanent($"/api/items/{id}");

    // Redirect DELETE /api/products/{id} → /api/items/{id}
    [HttpDelete("{id}")]
    public IActionResult RedirectDelete(string id) =>
        RedirectPermanent($"/api/items/{id}");

    // Redirect GET /api/products/{id}/suppliers → /api/items/{id}/suppliers
    [HttpGet("{id}/suppliers")]
    public IActionResult RedirectSuppliers(string id) =>
        RedirectPermanent($"/api/items/{id}/suppliers");

    // Redirect POST /api/products/{id}/suppliers → /api/items/{id}/suppliers
    [HttpPost("{id}/suppliers")]
    public IActionResult RedirectAddSupplier(string id) =>
        RedirectPermanent($"/api/items/{id}/suppliers");
}
