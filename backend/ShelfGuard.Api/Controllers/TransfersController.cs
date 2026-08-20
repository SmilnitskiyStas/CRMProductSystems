using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize(Policy = AppPolicies.CanReceiveStock)]
public sealed class TransfersController : ControllerBase
{
    private readonly ITransferService _transfers;

    public TransfersController(ITransferService transfers) => _transfers = transfers;

    [HttpGet]
    [Authorize(Policy = AppPolicies.CanViewStock)]
    [ProducesResponseType(typeof(PagedResult<TransferDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? store_id,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _transfers.GetPagedAsync(store_id, status, query.ClampedPage, query.ClampedPageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.CanViewStock)]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var transfer = await _transfers.GetByIdAsync(id, ct);
        return transfer is null ? NotFound() : Ok(transfer);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateTransferRequest request, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (transfer, error) = await _transfers.CreateAsync(tenantId.Value, userId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = transfer!.Id }, transfer);
    }

    [HttpPut("{id:guid}/confirm")]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var role = User.FindFirstValue(ClaimTypes.Role);
        var (transfer, error) = await _transfers.ConfirmAsync(id, userId.Value, tenantId.Value, role, ct);

        if (error == "Transfer not found.") return NotFound();
        if (error == "You do not have access to confirm transfers for this store.") return Forbid();
        if (error is not null) return BadRequest(new { error });

        return Ok(transfer);
    }

    [HttpPut("{id:guid}/cancel")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(TransferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var (transfer, error) = await _transfers.CancelAsync(id, ct);

        if (error == "Transfer not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(transfer);
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
