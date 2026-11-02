using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Authorization;
namespace ShelfGuard.Api.Controllers;
[ApiController,Route("api/erp/production-requests/desktop"),Authorize(Policy=AppPolicies.ProductionView)]
public sealed class RequestDesktopController(IRequestDesktop desktop,IRequestShipment shipment):ControllerBase
{
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web){NumberHandling=JsonNumberHandling.WriteAsString};
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return new JsonResult(new{protocol=RequestDesktopRules.Protocol,data=await action()},Wire);}
        catch(ArgumentException){return BadRequest(new{code="invalid_request_desktop"});}
        catch(InventoryPostingNotFoundException){return NotFound(new{code="request_not_found"});}
        catch(InventoryPostingConflictException){return Conflict(new{code="request_desktop_conflict",error="Оновіть заявку та план виробництва."});}
    }
    [HttpGet("capabilities")]public Task<IActionResult> Access(CancellationToken ct)=>Run(async()=>await desktop.AccessAsync(ct));
    [HttpGet]public Task<IActionResult> List([FromQuery]Guid locationId,[FromQuery]string side,[FromQuery]string? status,[FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await desktop.ListAsync(new(locationId,side,status,search,page),ct));
    [HttpGet("items")]public Task<IActionResult> Items([FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await desktop.ItemsAsync(search,page,ct));
    [HttpGet("destinations")]public Task<IActionResult> Destinations(CancellationToken ct)=>Run(async()=>await desktop.DestinationsAsync(ct));
    [HttpGet("{id:guid}")]public Task<IActionResult> Get(Guid id,[FromQuery]Guid locationId,[FromQuery]string side,CancellationToken ct)=>Run(async()=>await desktop.GetAsync(id,locationId,side,ct));
    [HttpGet("{id:guid}/shipment-preview"),RequireModule("production"),Authorize(Policy=AppPolicies.ProductionManage)]public Task<IActionResult> ShipmentPreview(Guid id,[FromQuery]Guid operationId,[FromQuery]Guid locationId,CancellationToken ct)=>Run(async()=>await shipment.PreviewAsync(id,operationId,locationId,ct));
    [HttpPost("{id:guid}/ship"),RequireModule("production"),Authorize(Policy=AppPolicies.ProductionManage),RequireRecentMfa]public Task<IActionResult> Ship(Guid id,[FromQuery]Guid locationId,RequestShipmentCommand command,CancellationToken ct)=>Run(async()=>await shipment.ShipAsync(id,locationId,command,ct));
    [HttpPost,Authorize(Policy=AppPolicies.ProductionManage),RequireRecentMfa]public Task<IActionResult> Create(RequestDesktopCreate command,CancellationToken ct)=>Run(async()=>await desktop.CreateAsync(command,ct));
    [HttpPost("{id:guid}/{phase}"),RequireRecentMfa]public Task<IActionResult> Decide(Guid id,string phase,[FromQuery]Guid locationId,[FromQuery]string side,RequestDesktopDecision command,CancellationToken ct)=>Run(async()=>await desktop.DecideAsync(id,locationId,side,phase,command,ct));
}
