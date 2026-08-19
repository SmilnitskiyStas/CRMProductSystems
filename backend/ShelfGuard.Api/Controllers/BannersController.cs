using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Banners;
using ShelfGuard.Application.Features.Banners.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Admin management of consumer-app promotional banners (Consumer App plan, TASK-521).
/// Same gate as LoyaltySettingsController — enterprise_admin+ only. DELETE never hard-deletes;
/// it flips Banner.IsActive to false (see Banner's own doc comment).
/// </summary>
[ApiController]
[Route("api/banners")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class BannersController : ControllerBase
{
    private readonly IBannerService _service;
    private readonly ITenantContext _tenantContext;

    public BannersController(IBannerService service, ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    /// <summary>List banners for the current tenant. Supports ?locationId and ?activeOnly filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BannerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? locationId = null,
        [FromQuery] bool? activeOnly = null,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var result = await _service.GetAllAsync(tenantId.Value, new BannerListQuery(locationId, activeOnly), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BannerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (banner, error) = await _service.GetByIdAsync(tenantId.Value, id, ct);
        return banner is null ? NotFound(new { error }) : Ok(banner);
    }

    /// <summary>Creates a banner and its location/product assignments in one transaction.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BannerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBannerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (banner, error) = await _service.CreateAsync(tenantId.Value, ResolveUserId(), request, ct);
        if (banner is null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = banner.Id }, banner);
    }

    /// <summary>Full replace, including locationIds/productIds.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BannerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBannerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (banner, error) = await _service.UpdateAsync(tenantId.Value, id, request, ct);
        if (banner is null)
            return error == "Banner not found." ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(banner);
    }

    /// <summary>First-publish — sets PublishedAt (idempotent, no-op if already published).</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(BannerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (banner, error) = await _service.PublishAsync(tenantId.Value, id, ct);
        return banner is null ? NotFound(new { error }) : Ok(banner);
    }

    /// <summary>Soft-hide — sets IsActive=false. Never a hard delete.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (success, error) = await _service.DeactivateAsync(tenantId.Value, id, ct);
        return success ? NoContent() : NotFound(new { error });
    }

    /// <summary>Uploads/replaces the banner's image. Same 5MB cap and format allowlist as ItemsController.UploadImage.</summary>
    [HttpPost("{id:guid}/image")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "File too large. Max 5 MB." });

        using var stream = file.OpenReadStream();
        var (url, error) = await _service.UploadImageAsync(tenantId.Value, id, stream, file.FileName, ct);

        if (error == "Banner not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });

        return Ok(new { imageUrl = url });
    }

    /// <summary>Views/clicks/CTR aggregated from banner_events.</summary>
    [HttpGet("{id:guid}/analytics")]
    [ProducesResponseType(typeof(BannerAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnalytics(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (analytics, error) = await _service.GetAnalyticsAsync(tenantId.Value, id, ct);
        return analytics is null ? NotFound(new { error }) : Ok(analytics);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
