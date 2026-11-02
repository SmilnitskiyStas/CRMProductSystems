using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/erp/write-offs"), Authorize(Policy = AppPolicies.AtLeastStoreManager), RequireModule("inventory")]
public sealed class WriteOffPostingController(IWriteOffPosting posting) : ControllerBase
{
    [HttpGet("drafts/capabilities")]
    public IActionResult DraftCapabilities() => Ok(new { supportsIdempotentDrafts = true });
    [HttpPost("drafts")]
    public Task<IActionResult> CreateDraft(WriteOffDraftCommand command, CancellationToken ct) => Execute(async () => await posting.CreateDraftAsync(command, ct));
    [HttpGet("{id:guid}/{phase}/preview")]
    public Task<IActionResult> Preview(Guid id, string phase, CancellationToken ct) => Execute(async () => await posting.PreviewAsync(id, phase, ct));
    [HttpPost("{id:guid}/{phase}"), RequireRecentMfa]
    public Task<IActionResult> Post(Guid id, string phase, WriteOffPostingCommand command, CancellationToken ct) => Execute(async () => await posting.PostAsync(id, phase, command, ct));
    private async Task<IActionResult> Execute(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_writeoff_posting", error = "Review exact quantities, reason and source values." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "writeoff_not_found", error = "Write-off not found." }); }
        catch (InventoryPostingDisabledException) { return Conflict(new { code = "inventory_posting_disabled", error = "ERP write-off operations are unavailable for this location." }); }
        catch (InventoryPostingConflictException) { return Conflict(new { code = "writeoff_posting_conflict", error = "Source or stock changed, or requires reconciliation. Review before posting." }); }
    }
}
