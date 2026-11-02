using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController, Route("api/erp/pricing/desktop"), Authorize(Policy = AppPolicies.CanViewStock), RequireModule("inventory")]
public sealed class PricingDesktopController(IPricingDesktop pricing) : ControllerBase
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.WriteAsString };
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return new JsonResult(new { protocol = PricingRules.Protocol, data = await action() }, Wire); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_pricing", error = "Перевірте ціни, період дії та реквізити прайсу." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "pricing_not_found" }); }
        catch (PricingConflict e) { return Conflict(new { code = e.Code, error = e.Message }); }
    }
    [HttpGet("capabilities")] public Task<IActionResult> Access(CancellationToken ct) => Run(async () => await pricing.AccessAsync(ct));
    [HttpGet("lists")] public Task<IActionResult> Lists(int page = 1, CancellationToken ct = default) => Run(async () => await pricing.ListsAsync(page, ct));
    [HttpGet("lists/{key:guid}")] public Task<IActionResult> List(Guid key, CancellationToken ct) => Run(async () => await pricing.ListAsync(key, ct));
    [HttpPost("save"), RequireRecentMfa] public Task<IActionResult> Save(PricingSave command, CancellationToken ct) => Run(async () => await pricing.SaveAsync(command, ct));
    [HttpPost("preview")] public Task<IActionResult> Preview(PricingPreview command, CancellationToken ct) => Run(async () => await pricing.PreviewAsync(command, ct));
    [HttpGet("snapshots/{id:guid}")] public Task<IActionResult> Snapshot(Guid id, CancellationToken ct) => Run(async () => await pricing.SnapshotAsync(id, ct));
}
