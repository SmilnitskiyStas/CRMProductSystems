using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Tenant-admin version history and rollback for the mobile config domain (TASK-545). Separate
/// controller from <see cref="MobileConfigPublishController"/> — same "one controller per concern"
/// precedent that controller's own remarks already established for draft-CRUD vs. publish; history
/// query and rollback are yet another concern, both scoped under <c>/api/v1/mobile/config/versions</c>
/// since a rollback target IS one of the rows the history list surfaces. Same
/// <c>AtLeastEnterpriseAdmin</c> / <see cref="ITenantContext"/> / <c>GetUserId()</c>-from-JWT shape as
/// <see cref="MobileConfigDraftController"/>/<see cref="MobileConfigPublishController"/>.
/// </summary>
[ApiController]
[Route("api/v1/mobile/config/versions")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class MobileConfigVersionsController : ControllerBase
{
    private readonly IMobileConfigVersionHistoryService _history;
    private readonly IMobileConfigPublishService _publish;
    private readonly ITenantContext _tenantContext;

    public MobileConfigVersionsController(
        IMobileConfigVersionHistoryService history, IMobileConfigPublishService publish, ITenantContext tenantContext)
    {
        _history = history;
        _publish = publish;
        _tenantContext = tenantContext;
    }

    /// <summary>Every version for the tenant, newest-first by <c>Version</c> descending.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MobileConfigVersionSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var versions = await _history.GetHistoryAsync(tenantId.Value, ct);
        return Ok(versions.Select(MobileConfigVersionSummaryResponse.From).ToList());
    }

    /// <summary>
    /// Publishes a new version cloned from the historical version <paramref name="versionId"/> — see
    /// <see cref="IMobileConfigPublishService.RollbackAsync"/> for the full sequence, including the
    /// required theme restoration. <c>404</c> covers an unknown/foreign-tenant version id; <c>400</c>
    /// covers "already the current version" and validation failure; <c>409</c> is a safe-to-retry
    /// concurrent-publish conflict — same error-shape conventions as
    /// <see cref="MobileConfigPublishController.Publish"/>.
    /// </summary>
    [HttpPost("{versionId:guid}/rollback")]
    [ProducesResponseType(typeof(MobileConfigPublishedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rollback(Guid versionId, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (published, error) = await _publish.RollbackAsync(tenantId.Value, versionId, GetUserId(), ct);
        if (error is not null)
        {
            return error.Type switch
            {
                MobileConfigPublishErrorType.ValidationFailed => BadRequest(new { errors = error.ValidationErrors }),
                MobileConfigPublishErrorType.ConcurrentPublish => Conflict(new { error = error.Message }),
                MobileConfigPublishErrorType.VersionNotFound => NotFound(new { error = error.Message }),
                MobileConfigPublishErrorType.CannotRollbackToCurrentVersion => BadRequest(new { error = error.Message }),
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
