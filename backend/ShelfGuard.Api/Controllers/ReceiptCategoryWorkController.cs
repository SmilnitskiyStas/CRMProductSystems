using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController, Route("api/receipts/{id:guid}/category-work")]
[Authorize(Policy = AppPolicies.CanReceiveStock), RequireModule("procurement")]
public sealed class ReceiptCategoryWorkController(IReceiptCategoryWork work) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Result(() => work.GetAsync(id, ct));

    [HttpPost, RequestSizeLimit(8 * 1024 * 1024)]
    public Task<IActionResult> Change(Guid id, ReceiptCategoryCommand body, CancellationToken ct) =>
        Result(() => work.ChangeAsync(id, body.Action, body.Key, body.Save, ct, body.LineIds));

    private async Task<IActionResult> Result(Func<Task<ReceiptWorkDto>> action)
    {
        try { return Ok(await action()); }
        catch (InventoryPostingNotFoundException) { return NotFound(); }
        catch (InventoryPostingConflictException e) { return Conflict(new { code = "receipt_category_conflict", error = e.Message }); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "55P03" or "40001" or "40P01") { return Conflict(new { code = "receipt_category_conflict", error = "Інший менеджер зберігає накладну. Оновіть стан і повторіть дію." }); }
        catch (ArgumentException e) { return BadRequest(new { code = "invalid_receipt_category", error = e.Message }); }
    }
}
public sealed record ReceiptCategoryCommand(string Action, string? Key, ReceiptCategorySave? Save, Guid[]? LineIds = null);
