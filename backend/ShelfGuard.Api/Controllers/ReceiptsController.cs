using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/receipts")]
[Authorize(Policy = AppPolicies.CanReceiveStock)]
public sealed class ReceiptsController : ControllerBase
{
    private readonly IReceiptService _receipts;

    public ReceiptsController(IReceiptService receipts) => _receipts = receipts;

    [HttpGet]
    [ProducesResponseType(typeof(List<ReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? store_id,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var receipts = await _receipts.GetAllAsync(store_id, status, ct);
        return Ok(receipts);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var receipt = await _receipts.GetByIdAsync(id, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPost]
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
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItems(Guid id, UpdateItemsRequest request, CancellationToken ct)
    {
        var (receipt, error) = await _receipts.UpdateItemsAsync(id, request, ct);

        if (error == "Receipt not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(receipt);
    }

    [HttpPut("{id:guid}/receive")]
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
