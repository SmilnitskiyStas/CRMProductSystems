using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/erp/transfers"), Authorize(Policy = AppPolicies.CanReceiveStock), RequireModule("inventory")]
public sealed class TransferPostingController(ITransferPosting posting) : ControllerBase
{
    [HttpPost("dispatch/preview")]
    public Task<IActionResult> PreviewDispatch(TransferDispatchCommand command, CancellationToken ct) => Execute(async () => await posting.PreviewDispatchAsync(command, ct));
    [HttpPost("dispatch"), RequireRecentMfa]
    public Task<IActionResult> Dispatch(TransferDispatchCommand command, CancellationToken ct) => Execute(async () => await posting.DispatchAsync(command, ct));
    [HttpGet("{id:guid}/{phase}/preview")]
    public Task<IActionResult> Preview(Guid id, string phase, CancellationToken ct) => Execute(async () => await posting.PreviewAsync(id, phase, ct));
    [HttpPost("{id:guid}/{phase}"), RequireRecentMfa]
    public Task<IActionResult> Complete(Guid id, string phase, TransferActionCommand command, CancellationToken ct) => Execute(async () => await posting.CompleteAsync(id, phase, command, ct));
    private async Task<IActionResult> Execute(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_transfer_posting", error = "Review transfer quantities, identities and cancellation reason." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "transfer_not_found", error = "Transfer or source batch not found." }); }
        catch (InventoryPostingDisabledException) { return Conflict(new { code = "inventory_posting_disabled", error = "ERP transfers are unavailable for these locations." }); }
        catch (InventoryPostingConflictException) { return Conflict(new { code = "transfer_posting_conflict", error = "Transfer or stock changed. Review before confirming." }); }
    }
}
