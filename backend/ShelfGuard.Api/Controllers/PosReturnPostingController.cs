using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;

[ApiController,Route("api/erp/pos-returns"),Authorize(Policy=AppPolicies.AtLeastStoreManager),RequireModule("inventory")]
public sealed class PosReturnPostingController(IPosReturnPosting posting) : ControllerBase
{
    [HttpGet("{saleId:guid}/preview")]
    public Task<IActionResult> Preview(Guid saleId,[FromQuery] Guid shiftId,CancellationToken ct)=>Execute(async()=>await posting.PreviewAsync(saleId,shiftId,ct));
    [HttpPost("{saleId:guid}"),RequireRecentMfa]
    public Task<IActionResult> Post(Guid saleId,PosReturnCommand command,CancellationToken ct)=>Execute(async()=>await posting.PostAsync(saleId,command,ct));
    [HttpPost("{saleId:guid}/fiscalize"),RequireRecentMfa]
    public Task<IActionResult> Fiscalize(Guid saleId,CancellationToken ct)=>Execute(async()=>await posting.FiscalizeAsync(saleId,ct));
    private async Task<IActionResult> Execute(Func<Task<object>> action)
    {
        try{return Ok(await action());}
        catch(ArgumentException){return BadRequest(new{code="invalid_pos_return",error="Review the full receipt, shift, request identity and reason."});}
        catch(InventoryPostingNotFoundException){return NotFound(new{code="pos_return_not_found"});}
        catch(InventoryPostingDisabledException){return Conflict(new{code="inventory_posting_disabled"});}
        catch(InventoryPostingConflictException){return Conflict(new{code="pos_return_conflict",error="Source, allocation or return policy requires fresh review."});}
    }
}
