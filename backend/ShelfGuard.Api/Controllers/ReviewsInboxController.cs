using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Reviews;
using ShelfGuard.Application.Features.Reviews.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Staff-side inbox for consumer purchase reviews (TASK-617). Same authorization tier as
/// <see cref="CustomerSupportInboxController"/> (AtLeastStoreManager) — an equivalent
/// customer-facing staff triage surface (viewing and replying to shopper reviews), not an
/// admin-only settings page. Deliberately NOT gated by [RequireModule], matching
/// CustomerSupportInboxController's own reasoning. Consumer-facing counterpart is
/// <see cref="ConsumerReviewsController"/>.
/// </summary>
[ApiController]
[Route("api/reviews")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class ReviewsInboxController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsInboxController(IReviewService reviews) => _reviews = reviews;

    /// <summary>Inbox reviews for the calling tenant, newest first, optionally filtered by rating, paged.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInbox(
        [FromQuery] short? rating, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var reviews = await _reviews.GetInboxAsync(tenantId.Value, rating, page, pageSize, ct);
        return Ok(reviews);
    }

    /// <summary>Staff reply; one reply per review (see IReviewService.ReplyAsync doc).</summary>
    [HttpPut("{id:guid}/reply")]
    [ProducesResponseType(typeof(PurchaseReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reply(
        Guid id, [FromBody] ReplyToPurchaseReviewRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (review, error, statusCode) = await _reviews.ReplyAsync(
            tenantId.Value, id, userId.Value, request.ReplyText, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(review);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
