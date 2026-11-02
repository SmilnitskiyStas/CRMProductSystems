using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController, Route("api/erp/labels/jobs"), Authorize(Policy = AppPolicies.CanViewStock), RequireModule("inventory"), RequestSizeLimit(131072)]
public sealed class LabelJobArchiveController(ILabelJobArchive archive) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { protocol = LabelJobArchiveRules.Protocol, data = await action() }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_label_job_archive", error = "Некоректний звіт про тестове завдання." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "label_job_archive_not_found" }); }
        catch (PricingConflict e) { return Conflict(new { code = e.Code, error = e.Message }); }
    }
    [HttpPost("sync")] public Task<IActionResult> Sync(LabelJobUpload command, CancellationToken ct) => Run(async () => await archive.UploadAsync(command, ct));
    [HttpGet] public Task<IActionResult> List(Guid locationId, int page = 1, bool allActors = false, int maxJobProtocol = 1, CancellationToken ct = default) => Run(async () => await archive.ListAsync(locationId, page, allActors, ct, maxJobProtocol));
    [HttpGet("{id:guid}")] public Task<IActionResult> Get(Guid id, Guid locationId, int maxJobProtocol = 1, CancellationToken ct = default) => Run(async () => await archive.GetAsync(locationId, id, ct, maxJobProtocol));
}
