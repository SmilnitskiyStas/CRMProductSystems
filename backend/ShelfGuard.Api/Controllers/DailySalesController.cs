using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Sales;
using ShelfGuard.Application.Features.Sales.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/daily-sales")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class DailySalesController : ControllerBase
{
    private readonly IDailySalesService _sales;

    public DailySalesController(IDailySalesService sales) => _sales = sales;

    [HttpGet]
    [ProducesResponseType(typeof(List<DailySaleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? store_id,
        [FromQuery] Guid? product_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var sales = await _sales.GetAsync(store_id, product_id, from, to, ct);
        return Ok(sales);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DailySaleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(UpsertDailySaleRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (sale, error) = await _sales.UpsertAsync(tenantId.Value, request, ct);
        if (error is not null)
            return error.EndsWith("not found.") ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(sale);
    }

    [HttpPost("import")]
    [ProducesResponseType(typeof(CsvImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ImportCsv(
        [FromQuery] Guid store_id,
        IFormFile file,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "CSV file is required (multipart field 'file')." });

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);

        var (result, error) = await _sales.ImportCsvAsync(tenantId.Value, store_id, content, ct);
        if (error is not null)
            return error.EndsWith("not found.") ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(result);
    }

    [HttpPut("{id:guid}/mark-anomaly")]
    [ProducesResponseType(typeof(DailySaleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAnomaly(Guid id, MarkAnomalyRequest request, CancellationToken ct)
    {
        var (sale, error) = await _sales.MarkAnomalyAsync(id, request.IsAnomaly, ct);
        if (error is not null)
            return NotFound(new { error });

        return Ok(sale);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value
            ?? User.FindFirstValue("tenant_id");
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }
}
