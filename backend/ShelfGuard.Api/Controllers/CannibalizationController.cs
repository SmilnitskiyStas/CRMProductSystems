using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Cannibalization;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/cannibalization")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class CannibalizationController : ControllerBase
{
    private readonly ICannibalizationService _cannibalization;

    public CannibalizationController(ICannibalizationService cannibalization) =>
        _cannibalization = cannibalization;

    [HttpGet("{discountId:guid}")]
    [ProducesResponseType(typeof(List<CannibalizationRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrGenerate(Guid discountId, CancellationToken ct)
    {
        var (rows, error) = await _cannibalization.GetOrGenerateAsync(discountId, ct);
        return error is not null ? NotFound(new { error }) : Ok(rows);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CannibalizationRowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateCannibalizationRequest request, CancellationToken ct)
    {
        var (row, error) = await _cannibalization.UpdateAsync(id, request, ct);
        if (error == "Cannibalization row not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(row);
    }

    [HttpPost("apply/{discountId:guid}")]
    [ProducesResponseType(typeof(ApplyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Apply(Guid discountId, CancellationToken ct)
    {
        var (result, error) = await _cannibalization.ApplyAsync(discountId, ct);
        if (error == "Discount not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(result);
    }
}
