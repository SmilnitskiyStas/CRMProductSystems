using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Reviews.Dtos;

namespace ShelfGuard.Application.Features.Reviews;

/// <summary>
/// Consumer review of a specific completed purchase (TASK-617) — mirrors
/// <c>IConsumerSupportService</c>'s shape (consumer-facing methods + staff-facing methods,
/// sharing the <c>(Dto?, string? Error, int? StatusCode)</c> return-tuple convention used by
/// <c>LoyaltyService</c>/<c>ConsumerSupportService</c>). Unlike support tickets, a review is
/// keyed to a <c>PosTransaction</c> rather than freely created — see
/// <see cref="CreateReviewAsync"/>'s doc for the ownership check this requires.
/// </summary>
public interface IReviewService
{
    // ── Consumer side ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a review for <paramref name="posTransactionId"/>, after verifying it is actually
    /// this consumer's own purchase. <c>PosTransaction</c> carries no direct
    /// <c>ConsumerAccountId</c> FK (only the tenant CRM <c>CustomerId</c>) — ownership is
    /// resolved via <c>LoyaltyLedgerEntry.PosTransactionId → MembershipId →
    /// LoyaltyMembership.ConsumerAccountId</c>, the only persisted signal a sale happened under
    /// a loyalty membership (see <c>ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync</c>'s
    /// own doc). A transaction with no loyalty ledger link at all (walk-in customer, never
    /// enrolled) cannot be reviewed — 403, an accepted limitation per the approved plan
    /// (`goofy-bubbling-naur.md` §2). Rejects with 409 when a review already exists for this
    /// transaction (checked both here and, as a DB-level backstop for a genuine race, via
    /// <c>DuplicateReviewException</c>).
    /// </summary>
    Task<(PurchaseReviewDto? Review, string? Error, int? StatusCode)> CreateReviewAsync(
        Guid consumerAccountId, Guid tenantId, Guid posTransactionId, int rating, string? comment,
        CancellationToken ct = default);

    /// <summary>This consumer's own reviews at one tenant, newest first, paged.</summary>
    Task<PagedResult<PurchaseReviewDto>> GetMyReviewsAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    // ── Staff side ────────────────────────────────────────────────────────────

    /// <summary>Every review at this tenant, newest first, optionally filtered by rating, paged.</summary>
    Task<PagedResult<PurchaseReviewDto>> GetInboxAsync(
        Guid tenantId, short? ratingFilter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>One reply per review — rejects with 409 if <c>ReplyText</c> is already set (the
    /// entity's own documented intent; unlike SupplierReview's reply endpoint, which allows a
    /// silent overwrite, TASK-617's brief calls for an explicit guard here).</summary>
    Task<(PurchaseReviewDto? Review, string? Error, int? StatusCode)> ReplyAsync(
        Guid tenantId, Guid reviewId, Guid staffUserId, string replyText, CancellationToken ct = default);
}
