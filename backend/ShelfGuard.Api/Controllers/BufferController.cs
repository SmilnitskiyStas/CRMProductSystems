using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Buffer;
using ShelfGuard.Application.Features.Buffer.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/buffer")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class BufferController : ControllerBase
{
    private readonly IBufferService _buffer;

    public BufferController(IBufferService buffer) => _buffer = buffer;

    [HttpGet("{storeId:guid}/{productId:guid}")]
    [ProducesResponseType(typeof(BufferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid storeId, Guid productId, CancellationToken ct)
    {
        var (buffer, error) = await _buffer.GetAsync(storeId, productId, ct);
        return error is not null ? NotFound(new { error }) : Ok(buffer);
    }

    [HttpPost("recalculate")]
    [ProducesResponseType(typeof(RecalculateBuffersResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recalculate(RecalculateBuffersRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _buffer.RecalculateAsync(tenantId.Value, request.StoreId, ct);
        if (error is not null)
            return NotFound(new { error });

        return Ok(result);
    }

    private Guid? GetTenantId()
    {
        var tenantIdStr = User.FindFirstValue("tenant_id");
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }
}
