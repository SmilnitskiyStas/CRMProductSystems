using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Common;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/private-files")]
[Authorize]
public sealed class PrivateFilesController(AppDbContext db, IAuthorizationService authorization) : ControllerBase
{
    public string StorageRoot { get; init; } = PrivateUploadPaths.Root;
    public string LegacyRoot { get; init; } = "wwwroot/uploads";

    [HttpGet("{category}/{tenantId:guid}/{fileName}")]
    public async Task<IActionResult> Get(string category, Guid tenantId, string fileName, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out var callerTenant) || callerTenant != tenantId
            || !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return NotFound();

        var url = $"/uploads/{category}/{tenantId}/{fileName}";
        if (!PrivateUploadPaths.TryParse(url, out _, out _, out _)) return NotFound();
        var policy = category == "contract-assets" ? AppPolicies.SupplierCabinet : AppPolicies.CanViewStock;
        if (!(await authorization.AuthorizeAsync(User, null, policy)).Succeeded) return Forbid();

        var allowed = false;
        if (category == "contract-assets")
        {
            allowed = await db.SupplierContractSettings.AsNoTracking().AnyAsync(s => s.TenantId == tenantId
                && (s.SignatureImageUrl == url || s.StampImageUrl == url), ct);
        }
        else if (category == "shelf-scans")
        {
            var locationId = await db.ShelfScans.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.ImagePath == url)
                .Select(s => (Guid?)s.LocationId).FirstOrDefaultAsync(ct);
            allowed = locationId.HasValue && await CanReadLocation(tenantId, userId, locationId.Value, ct);
        }
        else if (category == "floor-plans")
        {
            // New filenames bind the upload to its location even before the layout is saved.
            var prefix = fileName.Split('_')[0];
            if (fileName.Contains('_') && Guid.TryParseExact(prefix, "D", out var locationId))
                allowed = await CanReadLocation(tenantId, userId, locationId, ct);
            else
            {
                // Legacy names have no location id: require a reference in an authorized layout.
                var layouts = await db.Locations.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && l.FloorPlan != null)
                    .Select(l => new { l.Id, l.FloorPlan }).ToListAsync(ct);
                foreach (var layout in layouts)
                    if (ReferencesBackground(layout.FloorPlan!, url)
                        && await CanReadLocation(tenantId, userId, layout.Id, ct))
                    { allowed = true; break; }
            }
        }
        // Contracts are never served generically: use existing agreement-party download APIs.
        if (!allowed) return NotFound();
        var diskPath = PrivateUploadPaths.GetReadPath(url, StorageRoot, LegacyRoot);
        if (diskPath is null || !System.IO.File.Exists(diskPath)) return NotFound();
        var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null,
        };
        if (contentType is null) return NotFound();
        return PhysicalFile(Path.GetFullPath(diskPath), contentType);
    }

    private async Task<bool> CanReadLocation(Guid tenantId, Guid userId, Guid locationId, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == locationId && l.TenantId == tenantId, ct)) return false;
        if (User.IsInRole(AppRoles.EnterpriseAdmin) || User.IsInRole(AppRoles.Provider)
            || User.IsInRole(AppRoles.ProviderAdmin)) return true;
        // Use live assignments, not store_id (which is only a default/home-store hint).
        return await db.UserLocations.AnyAsync(l => l.TenantId == tenantId
            && l.UserId == userId && l.LocationId == locationId, ct);
    }

    private static bool ReferencesBackground(string json, string url)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("background", out var background)
                && background.ValueKind == JsonValueKind.Object
                && background.TryGetProperty("url", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == url;
        }
        catch (JsonException) { return false; }
    }
}
