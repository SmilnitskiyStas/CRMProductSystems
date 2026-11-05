using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController,Route("api/erp/production/desktop"),Authorize(Policy=AppPolicies.ProductionView),RequireModule("production")]
public sealed class ProductionDesktopController(IProductionDesktop desktop,IProductionPostingPreview preview):ControllerBase
{
    // Separate wire format: legacy numeric preview and its fingerprint protocol stay unchanged.
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web){NumberHandling=JsonNumberHandling.WriteAsString};
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return new JsonResult(new{protocol=ProductionDesktopRules.Protocol,data=await action()},Wire);}
        catch(ArgumentException){return BadRequest(new{code="invalid_production_desktop"});}
        catch(InventoryPostingNotFoundException){return NotFound(new{code="production_source_not_found"});}
        catch(InventoryPostingDisabledException){return Conflict(new{code="inventory_posting_disabled"});}
        catch(InventoryPostingConflictException){return Conflict(new{code="production_desktop_conflict",error="Review production source and scope readiness."});}
    }
    [HttpGet("capabilities")]public Task<IActionResult> Capabilities(CancellationToken ct)=>Run(async()=>await desktop.AccessAsync(ct));
    [HttpGet("orders/{id:guid}")]public Task<IActionResult> Order(Guid id,[FromQuery]Guid locationId,CancellationToken ct)=>Run(async()=>await desktop.OrderAsync(id,locationId,ct));
    [HttpGet("orders")]public Task<IActionResult> Orders([FromQuery]Guid locationId,[FromQuery]string? status,[FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await desktop.OrdersAsync(locationId,status,search,page,ct));
    [HttpGet("recipes")]public Task<IActionResult> Recipes([FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await desktop.RecipesAsync(search,page,ct));
    [HttpPost("orders"),Authorize(Policy=AppPolicies.ProductionManage)]public Task<IActionResult> Create(ProductionDesktopCreate command,CancellationToken ct)=>Run(async()=>await desktop.CreateAsync(command,ct));
    [HttpGet("orders/{id:guid}/{phase}/preview")]public Task<IActionResult> DecisionPreview(Guid id,string phase,CancellationToken ct)=>Run(async()=>await desktop.ReviewAsync(id,phase,ct));
    [HttpPost("orders/{id:guid}/{phase}"),RequireRecentMfa]public Task<IActionResult> Decide(Guid id,string phase,ProductionDesktopDecision command,CancellationToken ct)=>Run(async()=>await desktop.DecideAsync(id,phase,command,ct));
    [HttpGet("orders/{id:guid}/completion-preview"),Authorize(Policy=AppPolicies.ProductionComplete)]public Task<IActionResult> CompletionPreview(Guid id,CancellationToken ct)=>Run(async()=>await preview.PreviewAsync(id,ct));
}
