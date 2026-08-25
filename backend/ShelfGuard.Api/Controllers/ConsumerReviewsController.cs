using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Reviews;
using ShelfGuard.Application.Features.Reviews.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Consumer-facing purchase review channel (TASK-617) — requires a ConsumerAccount session JWT
/// (claim "consumer_account_id"), never a staff token. Same authorization shape as
/// <see cref="ConsumerSupportController"/>/<see cref="ConsumerProfileController"/>: the claim is
/// the whole app-level boundary (belt-and-suspenders alongside purchase_reviews' own
/// consumer_self_access RLS policy). Staff-side counterpart is
/// <see cref="ReviewsInboxController"/>.
/// </summary>
[ApiController]
[Route("api/consumer/reviews")]
[Authorize]
public sealed class ConsumerReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ConsumerReviewsController(IReviewService reviews) => _reviews = reviews;

    [HttpPost]
    [ProducesResponseType(typeof(PurchaseReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseReviewRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (review, error, statusCode) = await _reviews.CreateReviewAsync(
            consumerId.Value, request.TenantId, request.PosTransactionId, request.Rating, request.Comment, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return StatusCode(StatusCodes.Status201Created, review);
    }

    /// <summary><paramref name="tenantId"/> is required — a consumer's reviews live per-tenant
    /// (mirrors ConsumerSupportController.GetMyTickets taking TenantId as a query param).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyReviews(
        [FromQuery] Guid tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var reviews = await _reviews.GetMyReviewsAsync(consumerId.Value, tenantId, page, pageSize, ct);
        return Ok(reviews);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Mirrors ConsumerSupportController.ResolveConsumerAccountId exactly — a staff JWT
    /// never carries this claim.</summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
