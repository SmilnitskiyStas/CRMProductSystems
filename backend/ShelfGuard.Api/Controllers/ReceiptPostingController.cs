using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/erp/receipts")]
[Authorize(Policy = AppPolicies.CanReceiveStock)]
[RequireModule("procurement")]
public sealed class ReceiptPostingController(IReceiptPosting posting, IReceiptService receipts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery(Name = "store_id")] Guid locationId, int page = 1, string? search = null, string? status = null, CancellationToken ct = default)
    {
        try { return Ok(new { protocol = "receipts.desktop.v1", data = await posting.ListAsync(locationId, page, search ?? "", status ?? "", ct) }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_receipt_query" }); }
        catch (InventoryPostingNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:guid}/receiving")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        try { return Ok(await posting.PreviewAsync(id, ct)); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "receipt_not_found", error = "Receipt not found." }); }
    }

    [HttpPost("{id:guid}/receive"), RequireRecentMfa]
    public async Task<IActionResult> Receive(Guid id, ReceiptPostingBody body, CancellationToken ct)
    {
        try
        {
            var result = await posting.ReceiveAsync(id, body.ExpectedFingerprint!, ct);
            var receipt = await receipts.GetByIdAsync(id, ct) ?? throw new InventoryPostingNotFoundException();
            return Ok(receipt with { ErpDocumentId = result.DocumentId, ErpPostingId = result.PostingId, ErpReplayed = result.Replayed });
        }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_receipt_posting", error = "Review quantities, batch details and purchase values before receiving." }); }
        catch (InventoryPostingNotFoundException) { return NotFound(new { code = "receipt_not_found", error = "Receipt not found." }); }
        catch (InventoryPostingDisabledException) { return Conflict(new { code = "inventory_posting_disabled", error = "ERP receiving is disabled for this location." }); }
        catch (InventoryPostingConflictException) { return Conflict(new { code = "receipt_posting_conflict", error = "Receipt changed or is not ready. Review it before receiving." }); }
    }
}
public sealed class ReceiptPostingBody
{
    [Required, RegularExpression("^[0-9a-f]{64}$")] public string? ExpectedFingerprint { get; init; }
}
