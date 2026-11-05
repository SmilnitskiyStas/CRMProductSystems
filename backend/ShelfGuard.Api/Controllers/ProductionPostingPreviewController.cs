using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;

[ApiController,Route("api/erp/production"),Authorize(Policy=AppPolicies.ProductionComplete),RequireModule("production")]
public sealed class ProductionPostingPreviewController(IProductionPostingPreview preview):ControllerBase
{
    [HttpGet("{orderId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid orderId,CancellationToken ct)
    {
        try{return Ok(await preview.PreviewAsync(orderId,ct));}
        catch(ArgumentException){return BadRequest(new{code="invalid_production_review"});}
        catch(InventoryPostingNotFoundException){return NotFound(new{code="production_order_not_found"});}
        catch(InventoryPostingConflictException){return Conflict(new{code="production_review_conflict",error="Production source requires reconciliation or exceeds supported review limits."});}
    }
}
