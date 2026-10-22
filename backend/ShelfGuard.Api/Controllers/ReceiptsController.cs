using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

// ADR-020 (TASK-346): no class-level policy. Get* keep the pre-existing CanReceiveStock floor
// OR "receipts.view". Create/UpdateItems/Receive/Cancel are deliberately untouched — the
// write-heavy stock path stays role-gated only (explicit per-action now, same effective
// policy as before), no capability bypass (ADR-020 point 3).
[ApiController]
[Route("api/receipts")]
public sealed class ReceiptsController : ControllerBase
{
    private readonly IReceiptService _receipts;
    private readonly IAutoReplenishmentService _autoReplenishment;

    public ReceiptsController(IReceiptService receipts, IAutoReplenishmentService autoReplenishment)
    {
        _receipts = receipts;
        _autoReplenishment = autoReplenishment;
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ReceiptsViewOrCapability)]
    [ProducesResponseType(typeof(PagedResult<ReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? store_id,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool? sortDescending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? category_id = null,
        [FromQuery] int? min_items = null,
        [FromQuery] int? max_items = null,
        [FromQuery] string? source = null,
        CancellationToken ct = default)
    {
        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _receipts.GetPagedAsync(
            store_id, status, search, sortBy, sortDescending, query.ClampedPage, query.ClampedPageSize,
            category_id, min_items, max_items, source, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.ReceiptsViewOrCapability)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var receipt = await _receipts.GetByIdAsync(id, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateReceiptRequest request, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (receipt, error) = await _receipts.CreateAsync(tenantId.Value, userId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = receipt!.Id }, receipt);
    }

    [HttpPut("{id:guid}/items")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItems(Guid id, UpdateItemsRequest request, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        var (receipt, error) = await _receipts.UpdateItemsAsync(id, request, ct, userId);

        if (error == "Receipt not found.") return NotFound();
        if (error == ReceiptErrors.TakenByOther) return Conflict(new { error });
        if (error is not null) return BadRequest(new { error });

        return Ok(receipt);
    }

    /// <summary>TASK-757: edit supplier / notes / expected date of a draft order.</summary>
    [HttpPut("{id:guid}/header")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateHeader(Guid id, UpdateReceiptHeaderRequest request, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();
        return DraftResult(await _receipts.UpdateHeaderAsync(id, request, userId, ct));
    }

    /// <summary>TASK-757: add a line to a draft (same product -> quantity is increased).</summary>
    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(Guid id, AddReceiptItemRequest request, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();
        return DraftResult(await _receipts.AddItemAsync(id, request, userId, ct));
    }

    /// <summary>TASK-757: remove a line from a draft (the last line cannot be removed).</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();
        return DraftResult(await _receipts.RemoveItemAsync(id, itemId, userId, ct));
    }

    /// <summary>TASK-757: confirm the draft and mark it as sent to the supplier (draft -> ordered). Status only, no delivery.</summary>
    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();
        return DraftResult(await _receipts.SendAsync(id, userId.Value, ct));
    }

    [HttpPut("{id:guid}/receive")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive(Guid id, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (receipt, error) = await _receipts.ReceiveAsync(id, userId.Value, ct);

        if (error == "Receipt not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(receipt);
    }

    /// <summary>
    /// TASK-755: take a draft order into work. 409 when another user holds it; idempotent for the
    /// same user. There is NO separate "confirm" step: the draft receipt IS the order (draft ->
    /// received via PUT /receive).
    /// </summary>
    [HttpPost("{id:guid}/take")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Take(Guid id, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (receipt, error) = await _receipts.TakeAsync(id, userId.Value, ct);
        if (error == ReceiptErrors.NotFound) return NotFound();
        if (error == ReceiptErrors.TakenByOther) return Conflict(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    /// <summary>TASK-755: release a taken draft (the taker, or network_manager / enterprise_admin / provider).</summary>
    [HttpPost("{id:guid}/release")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        var canOverride = role is "enterprise_admin" or "network_manager" or "provider";

        var (receipt, error) = await _receipts.ReleaseAsync(id, userId.Value, canOverride, ct);
        if (error == ReceiptErrors.NotFound) return NotFound();
        if (error == ReceiptErrors.ReleaseForbidden) return StatusCode(StatusCodes.Status403Forbidden, new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    /// <summary>
    /// TASK-755: runs the same service as the daily 07:00 cron for the caller's tenant (and optionally
    /// one store); returns the created/changed auto drafts (cancelled ones included).
    /// </summary>
    [HttpPost("auto-generate")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(List<ReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AutoGenerate([FromBody] AutoReplenishmentRequest? dto, CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var (drafts, error, code) = await _autoReplenishment.GenerateAsync(tenantId.Value, dto?.StoreLocationId, ct);
        return error is not null ? StatusCode(code ?? 400, new { error }) : Ok(drafts);
    }

    /// <summary>Moves the expected delivery date of a delayed order; the order stays open and "in transit".</summary>
    [HttpPut("{id:guid}/postpone")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Postpone(Guid id, PostponeReceiptRequest request, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (receipt, error) = await _receipts.PostponeAsync(id, userId.Value, request, ct);

        if (error == "Receipt not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(receipt);
    }

    [HttpPut("{id:guid}/cancel")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var (receipt, error) = await _receipts.CancelAsync(id, ct);

        if (error == "Receipt not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(receipt);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>404 not found, 409 taken-by-other / not a draft, 400 other validation errors.</summary>
    private IActionResult DraftResult((ReceiptDto? Receipt, string? Error) r)
    {
        if (r.Error == ReceiptErrors.NotFound) return NotFound();
        if (r.Error is ReceiptErrors.TakenByOther or ReceiptErrors.NotDraft) return Conflict(new { error = r.Error });
        if (r.Error is not null) return BadRequest(new { error = r.Error });
        return Ok(r.Receipt);
    }

    private (Guid? TenantId, Guid? UserId) GetContext()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;

        Guid.TryParse(userIdStr, out var userId);
        Guid.TryParse(tenantIdStr, out var tenantId);

        return (
            tenantId == Guid.Empty ? null : tenantId,
            userId == Guid.Empty ? null : userId
        );
    }
}
