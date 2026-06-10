using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.WriteOffs;
using ShelfGuard.Application.Features.WriteOffs.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/write-offs")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class WriteOffsController : ControllerBase
{
    private readonly IWriteOffService _writeOffs;

    public WriteOffsController(IWriteOffService writeOffs) => _writeOffs = writeOffs;

    [HttpGet]
    [ProducesResponseType(typeof(List<WriteOffDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? store_id,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var writeOffs = await _writeOffs.GetAllAsync(store_id, status, ct);
        return Ok(writeOffs);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WriteOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var writeOff = await _writeOffs.GetByIdAsync(id, ct);
        return writeOff is null ? NotFound() : Ok(writeOff);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(WriteOffDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateWriteOffRequest request, CancellationToken ct)
    {
        var (tenantId, userId) = GetContext();
        if (tenantId is null || userId is null) return Forbid();

        var (writeOff, error) = await _writeOffs.CreateAsync(tenantId.Value, userId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = writeOff!.Id }, writeOff);
    }

    [HttpPut("{id:guid}/approve")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(WriteOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (_, userId) = GetContext();
        if (userId is null) return Forbid();

        var (writeOff, error) = await _writeOffs.ApproveAsync(id, userId.Value, ct);

        if (error == "Write-off not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(writeOff);
    }

    [HttpPut("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(WriteOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var (writeOff, error) = await _writeOffs.RejectAsync(id, ct);

        if (error == "Write-off not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(writeOff);
    }

    // PDF generation placeholder — Puppeteer not implemented in v1
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GetPdf(Guid id) =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "PDF generation not yet implemented." });

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
