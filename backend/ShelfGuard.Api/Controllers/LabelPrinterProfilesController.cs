using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController, Route("api/erp/labels/printers"), Authorize(Policy = AppPolicies.CanViewStock), RequireModule("inventory"), RequestSizeLimit(16384)]
public sealed class LabelPrinterProfilesController(ILabelPrinterProfiles profiles) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { protocol = LabelPrinterRules.Protocol, data = await action() }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_printer_profile", error = "Перевірте протокол, DPI та розмір тестового профілю." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "printer_profile_not_found" }); }
        catch (PricingConflict e) { return Conflict(new { code = e.Code, error = e.Message }); }
    }
    [HttpGet] public Task<IActionResult> List(Guid locationId, int page = 1, CancellationToken ct = default) => Run(async () => await profiles.ListAsync(locationId, page, ct));
    [HttpGet("{key:guid}/history")] public Task<IActionResult> History(Guid key, Guid locationId, int page = 1, CancellationToken ct = default) => Run(async () => await profiles.HistoryAsync(locationId, key, page, ct));
    [HttpGet("revisions/{id:guid}")] public Task<IActionResult> Revision(Guid id, Guid locationId, CancellationToken ct) => Run(async () => await profiles.RevisionAsync(locationId, id, ct));
    [HttpPost("save"), RequireRecentMfa] public Task<IActionResult> Save(LabelPrinterSave command, CancellationToken ct) => Run(async () => await profiles.SaveAsync(command, ct));
}
