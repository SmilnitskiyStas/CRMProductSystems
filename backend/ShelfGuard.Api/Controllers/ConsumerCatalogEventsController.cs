using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/consumer/{tenantId:guid}/catalog-events")]
public sealed class ConsumerCatalogEventsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ITenantRepository _tenants; private readonly ITenantSessionOverride _scope;
    public ConsumerCatalogEventsController(AppDbContext db, ITenantRepository tenants, ITenantSessionOverride scope) { _db = db; _tenants = tenants; _scope = scope; }

    [HttpPost]
    public async Task<IActionResult> Record(Guid tenantId, [FromBody] RecordCatalogEventRequest request, CancellationToken ct)
    {
        if (!MobileCatalogEventType.IsValid(request.EventType)) return BadRequest(new { error = "Unsupported event type." });
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 100) return BadRequest(new { error = "Session id is required." });
        if (request.EventType != MobileCatalogEventType.CatalogView && !request.ProductId.HasValue) return BadRequest(new { error = "Product id is required." });
        if (await _tenants.GetByIdAsync(tenantId, ct) is null) return NotFound();
        var consumerId = Guid.TryParse(User.FindFirst("consumer_account_id")?.Value, out var parsed) ? parsed : (Guid?)null;
        var recorded = await _scope.ExecuteAsync(tenantId, async () =>
        {
            var catalogExists = await _db.MobileCatalogSettings.AnyAsync(x => x.Id == request.CatalogId && x.TenantId == tenantId
                && _db.MobileCatalogLocations.Any(l => l.SettingsId == x.Id && l.LocationId == request.StoreId), ct);
            if (!catalogExists) return false;
            if (request.ProductId.HasValue && !await _db.MobileCatalogItems.AnyAsync(x => x.SettingsId == request.CatalogId && x.ProductId == request.ProductId, ct)) return false;
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            var duplicate = await _db.MobileCatalogEvents.AnyAsync(x => x.TenantId == tenantId && x.CatalogId == request.CatalogId && x.StoreId == request.StoreId && x.EventType == request.EventType && x.ProductId == request.ProductId && x.SessionId == request.SessionId && x.OccurredAt >= cutoff, ct);
            if (!duplicate) { await _db.MobileCatalogEvents.AddAsync(new MobileCatalogEvent { TenantId = tenantId, CatalogId = request.CatalogId, StoreId = request.StoreId, ProductId = request.ProductId, ConsumerAccountId = consumerId, SessionId = request.SessionId.Trim(), EventType = request.EventType }, ct); await _db.SaveChangesAsync(ct); }
            return true;
        }, ct);
        return recorded ? NoContent() : NotFound();
    }
}

public sealed record RecordCatalogEventRequest(Guid CatalogId, Guid StoreId, Guid? ProductId, string EventType, string SessionId);
