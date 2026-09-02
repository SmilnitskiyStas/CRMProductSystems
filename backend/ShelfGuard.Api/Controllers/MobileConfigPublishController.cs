using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Tenant-admin trigger for <see cref="IMobileConfigPublishService.PublishAsync"/> (TASK-544).
///
/// No publish-trigger endpoint was scoped anywhere else in Stage D/E's registered task list
/// (checked <c>TASK-547</c>'s Preview API, which is explicitly staff-only GET of the current
/// draft and deliberately never touches publish) — a publish service with no way to invoke it is
/// incomplete, so this is added here rather than left for a later task. Separate controller
/// (not folded into <see cref="MobileConfigDraftController"/>) because <see cref="IMobileConfigDraftService"/>'s
/// own remarks are explicit that draft CRUD and publish are different concerns ("No publish or
/// consumer-facing read here — those are TASK-534 ... and TASK-544"); this also keeps the exact
/// route <c>POST /api/v1/mobile/config/publish</c> (sibling to, not nested under,
/// <c>/api/v1/mobile/config/draft</c>) without an absolute-route override on another controller.
/// Same <c>AtLeastEnterpriseAdmin</c> / <see cref="ITenantContext"/> / <c>GetUserId()</c>-from-JWT
/// shape as <see cref="MobileConfigDraftController"/>.
/// </summary>
[ApiController]
[Route("api/v1/mobile/config/publish")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[RequireModule("mobile_app")] // TASK-674: App Builder is part of the "Застосунок" section
public sealed class MobileConfigPublishController : ControllerBase
{
    private readonly IMobileConfigPublishService _publish;
    private readonly ITenantContext _tenantContext;

    public MobileConfigPublishController(IMobileConfigPublishService publish, ITenantContext tenantContext)
    {
        _publish = publish;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Publishes the tenant's current draft. <c>400</c> covers both "no draft to publish" and
    /// schema-validation failure (the latter carries <c>{ errors: [...] }</c>, matching
    /// <see cref="MobileConfigDraftController"/>'s existing validation-error shape); <c>409</c> is
    /// a safe-to-retry concurrent-publish conflict.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MobileConfigPublishedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (published, error) = await _publish.PublishAsync(tenantId.Value, GetUserId(), ct);
        if (error is not null)
        {
            return error.Type switch
            {
                MobileConfigPublishErrorType.ValidationFailed => BadRequest(new { errors = error.ValidationErrors }),
                MobileConfigPublishErrorType.ConcurrentPublish => Conflict(new { error = error.Message }),
                _ => BadRequest(new { error = error.Message }),
            };
        }

        return Ok(MobileConfigPublishedResponse.From(published!));
    }

    // ── helpers ────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
