using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController,Route("api/erp/catalog/desktop"),Authorize(Policy=AppPolicies.CanViewStock),RequireModule("inventory")]
public sealed class CatalogDesktopController(ICatalogDesktop catalog):ControllerBase
{
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web){NumberHandling=JsonNumberHandling.WriteAsString};
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {try{return new JsonResult(new{protocol=CatalogRules.Protocol,data=await action()},Wire);}catch(ArgumentException){return BadRequest(new{code="invalid_catalog",error="Перевірте реквізити та точність кількостей."});}catch(InventoryPostingNotFoundException){return NotFound(new{code="catalog_not_found"});}catch(CatalogConflict e){return Conflict(new{code=e.Code,error=e.Message});}}
    [HttpGet("capabilities")]public Task<IActionResult> Access(CancellationToken ct)=>Run(async()=>await catalog.AccessAsync(ct));
    [HttpGet("items")]public Task<IActionResult> Items([FromQuery]string? search,[FromQuery]string state="active",[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await catalog.ItemsAsync(search,state,page,ct));
    [HttpGet("items/selection")]public Task<IActionResult> Selection([FromQuery]Guid[] ids,CancellationToken ct)=>Run(async()=>await catalog.SelectionAsync(ids,ct));
    [HttpGet("items/{id:guid}")]public Task<IActionResult> Item(Guid id,CancellationToken ct)=>Run(async()=>await catalog.ItemAsync(id,ct));
    [HttpGet("recipes")]public Task<IActionResult> Recipes([FromQuery]string? search,[FromQuery]string state="active",[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await catalog.RecipesAsync(search,state,page,ct));
    [HttpGet("recipes/{id:guid}")]public Task<IActionResult> Recipe(Guid id,CancellationToken ct)=>Run(async()=>await catalog.RecipeAsync(id,ct));
    [HttpPost("save"),RequireRecentMfa]public Task<IActionResult> Save(CatalogSave command,CancellationToken ct)=>Run(async()=>await catalog.SaveAsync(command,ct));
}
