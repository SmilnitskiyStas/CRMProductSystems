using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/erp/inventory")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
[RequireModule("inventory")]
public sealed class InventoryPostingController(IInventoryAdjustmentPosting posting, IInventoryPostingStatusService status) : ControllerBase
{
    [HttpGet("scopes/{locationId:guid}")]
    public async Task<ActionResult<InventoryPostingStatus>> Scope(Guid locationId, CancellationToken ct) =>
        Ok(await status.GetAsync(locationId, ct));

    [HttpPost("adjustments")]
    [RequireRecentMfa]
    public async Task<ActionResult<InventoryAdjustmentReceiptBody>> Adjust(InventoryAdjustmentBody body, CancellationToken ct)
    {
        try
        {
            var result = await posting.AdjustAsync(new(body.RequestId!.Value, body.LocationId!.Value, body.BatchId!.Value,
                body.ExpectedQuantity!.Value, body.Quantity!.Value, body.Reason!), ct);
            return Ok(new InventoryAdjustmentReceiptBody(result.TenantId, result.RequestId, result.DocumentId, result.PostingId,
                result.EntryId, result.EventId, result.BatchId, result.QuantityBefore.ToString("0.00", CultureInfo.InvariantCulture),
                result.QuantityAfter.ToString("0.00", CultureInfo.InvariantCulture), result.PostedAt, result.Replayed));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Provide valid request/scope IDs, exact quantities and an adjustment reason.", code = "invalid_inventory_adjustment" });
        }
        catch (InventoryPostingDisabledException)
        {
            return Conflict(new { error = "ERP inventory posting is disabled for this location.", code = "inventory_posting_disabled" });
        }
        catch (InventoryPostingConflictException)
        {
            return Conflict(new { error = "Inventory changed or the scope is not ready. Review the count and posting status before retrying.", code = "inventory_posting_conflict" });
        }
        catch (InventoryPostingNotFoundException)
        {
            return NotFound(new { error = "Stock batch not found.", code = "stock_batch_not_found" });
        }
    }
}

/// <summary>Nullable required values distinguish a missing optimistic precondition from an explicit zero.</summary>
public sealed class InventoryAdjustmentBody
{
    [Required] public Guid? RequestId { get; init; }
    [Required] public Guid? LocationId { get; init; }
    [Required] public Guid? BatchId { get; init; }
    [Required, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public decimal? ExpectedQuantity { get; init; }
    [Required, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public decimal? Quantity { get; init; }
    [Required, StringLength(500)] public string? Reason { get; init; }
}

public sealed record InventoryAdjustmentReceiptBody(Guid TenantId, Guid RequestId, Guid DocumentId, Guid PostingId,
    Guid EntryId, Guid EventId, Guid BatchId, string QuantityBefore, string QuantityAfter, DateTime PostedAt, bool Replayed);
