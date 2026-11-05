using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController, Route("api/erp/labels/templates"), Authorize(Policy = AppPolicies.CanViewStock), RequireModule("inventory"), RequestSizeLimit(32768)]
public sealed class LabelTemplatesController(ILabelTemplates labels) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try { return Ok(new { protocol = LabelTemplateRules.Protocol, data = await action() }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_label_template", error = "Перевірте розміри, поля та редакцію шаблону." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "label_not_found" }); }
        catch (PricingConflict e) { return Conflict(new { code = e.Code, error = e.Message }); }
    }
    [HttpGet("capabilities")] public Task<IActionResult> Access(CancellationToken ct) => Run(async () => await labels.AccessAsync(ct));
    [HttpGet("revisions/{id:guid}")] public Task<IActionResult> Revision(Guid id, CancellationToken ct) => Run(async () => await labels.RevisionAsync(id, ct));
    [HttpGet] public Task<IActionResult> List(int page = 1, CancellationToken ct = default) => Run(async () => await labels.ListAsync(page, ct));
    [HttpGet("{key:guid}/history")] public Task<IActionResult> History(Guid key, int page = 1, CancellationToken ct = default) => Run(async () => await labels.HistoryAsync(key, page, ct));
    [HttpPost("save"), RequireRecentMfa] public Task<IActionResult> Save(LabelTemplateSave command, CancellationToken ct) => Run(async () => await labels.SaveAsync(command, ct));
    [HttpGet("assignments/{location:guid}")] public Task<IActionResult> Assignment(Guid location, CancellationToken ct) => Run(async () => await labels.AssignmentAsync(location, ct));
    [HttpPost("assign"), RequireRecentMfa] public Task<IActionResult> Assign(LabelAssignmentSave command, CancellationToken ct) => Run(async () => await labels.AssignAsync(command, ct));
}
