using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController,Route("api/erp/production"),Authorize(Policy=AppPolicies.ProductionComplete),RequireModule("production")]
public sealed class ProductionPostingController(IProductionPosting posting):ControllerBase
{
    [HttpPost("{orderId:guid}/complete"),RequireRecentMfa]
    public async Task<IActionResult> Complete(Guid orderId,ProductionPostingCommand command,CancellationToken ct)
    {
        try{return Ok(await posting.CompleteAsync(orderId,command,ct));}
        catch(ArgumentException){return BadRequest(new{code="invalid_production_posting"});}
        catch(InventoryPostingNotFoundException){return NotFound(new{code="production_order_not_found"});}
        catch(InventoryPostingDisabledException){return Conflict(new{code="inventory_posting_disabled"});}
        catch(InventoryPostingConflictException){return Conflict(new{code="production_posting_conflict",error="Review the order, recipe, stock and request allocations before confirming again."});}
    }
}
