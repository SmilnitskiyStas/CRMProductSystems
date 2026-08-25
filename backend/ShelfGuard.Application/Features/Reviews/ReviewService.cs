using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Reviews.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Reviews;

/// <summary>
/// See <see cref="IReviewService"/> for the responsibility split.
///
/// No <c>ITenantSessionOverride</c> anywhere in this class, unlike several other consumer-facing
/// services (LoyaltyService, ConsumerSupportService) — every table this feature touches under a
/// consumer session already carries either no RLS at all (none here) or a direct
/// <c>consumer_self_access</c> policy that admits the calling consumer's own rows without an
/// override: <c>purchase_reviews</c> (ConsumerAccountId column, TASK-613 migration),
/// <c>loyalty_memberships</c>/<c>loyalty_ledger_entries</c> (TASK-404), and <c>tenants</c> (no
/// tenant-scoping RLS on its own table — same reason ConsumerSupportService/LoyaltyService read
/// it directly).
/// </summary>
public sealed class ReviewService : IReviewService
{
    public const string ConsumerNotFoundError = "Consumer account not found.";
    public const string TenantNotFoundError = "Tenant not found.";
    public const string ReviewNotFoundError = "Review not found.";
    public const string RatingRangeError = "Rating must be between 1 and 5.";
    public const string AlreadyReviewedError = "You have already reviewed this purchase.";
    public const string AlreadyRepliedError = "This review already has a reply.";
    public const string ReplyRequiredError = "Reply text is required.";
    /// <summary>
    /// Deliberately one generic message for every way a transaction can fail the ownership
    /// check (doesn't belong to this consumer, belongs to a different one, or was never linked
    /// to any loyalty membership at all) — never discloses which, same "uniform rejection"
    /// reasoning ConsumerSupportService.GetTicketAsync uses for its 404s.
    /// </summary>
    public const string PurchaseNotReviewableError =
        "This purchase cannot be reviewed — it does not belong to a linked loyalty account for you.";

    public const int MaxCommentLength = 2000;
    public const int MaxReplyTextLength = 2000;

    private readonly IPurchaseReviewRepository _reviews;
    private readonly IConsumerAccountRepository _consumerAccounts;
    private readonly ITenantRepository _tenants;
    private readonly ILoyaltyRepository _loyalty;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IPurchaseReviewRepository reviews,
        IConsumerAccountRepository consumerAccounts,
        ITenantRepository tenants,
        ILoyaltyRepository loyalty,
        ILogger<ReviewService> logger)
    {
        _reviews = reviews;
        _consumerAccounts = consumerAccounts;
        _tenants = tenants;
        _loyalty = loyalty;
        _logger = logger;
    }

    // ── Consumer side ─────────────────────────────────────────────────────────

    public async Task<(PurchaseReviewDto? Review, string? Error, int? StatusCode)> CreateReviewAsync(
        Guid consumerAccountId, Guid tenantId, Guid posTransactionId, int rating, string? comment,
        CancellationToken ct = default)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, ConsumerNotFoundError, 404);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, TenantNotFoundError, 404);

        if (rating is < 1 or > 5)
            return (null, RatingRangeError, 400);

        var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (trimmedComment is { Length: > MaxCommentLength })
            return (null, $"Comment cannot exceed {MaxCommentLength} characters.", 400);

        if (!await IsOwnPurchaseAsync(consumerAccountId, tenantId, posTransactionId, ct))
            return (null, PurchaseNotReviewableError, 403);

        // Pre-check: the common case (no race) never needs to reach the DB's own unique index.
        var existing = await _reviews.GetByTransactionAsync(posTransactionId, ct);
        if (existing is not null)
            return (null, AlreadyReviewedError, 409);

        var review = new PurchaseReview
        {
            TenantId = tenantId,
            ConsumerAccountId = consumerAccountId,
            PosTransactionId = posTransactionId,
            Rating = (short)rating,
            Comment = trimmedComment,
        };

        await _reviews.AddAsync(review, ct);
        try
        {
            await _reviews.SaveChangesAsync(ct);
        }
        catch (DuplicateReviewException)
        {
            // DB-level backstop for a genuine race (two requests for the same PosTransactionId
            // landing concurrently) — the pre-check above already covers every non-racing call.
            return (null, AlreadyReviewedError, 409);
        }

        _logger.LogInformation(
            "Consumer {ConsumerId} reviewed purchase {PosTransactionId} for tenant {TenantId} ({Rating}/5).",
            consumerAccountId, posTransactionId, tenantId, rating);

        return (ToDto(review, consumer.FullName, consumer.Phone), null, null);
    }

    public async Task<PagedResult<PurchaseReviewDto>> GetMyReviewsAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _reviews.GetPagedForConsumerAsync(
            consumerAccountId, tenantId, clampedPage, clampedPageSize, ct);

        var consumer = await _consumerAccounts.GetByIdAsync(consumerAccountId, ct);
        var name = consumer?.FullName ?? "—";
        var phone = consumer?.Phone ?? "—";

        return new PagedResult<PurchaseReviewDto>
        {
            Items = items.Select(r => ToDto(r, name, phone)).ToList(),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
    }

    // ── Staff side ────────────────────────────────────────────────────────────

    public async Task<PagedResult<PurchaseReviewDto>> GetInboxAsync(
        Guid tenantId, short? ratingFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _reviews.GetPagedForTenantAsync(
            tenantId, ratingFilter, clampedPage, clampedPageSize, ct);

        // Per-page name/phone cache — mirrors ConsumerSupportService.ToDtosForStaffAsync (an
        // inbox page can span many different consumers).
        var consumerCache = new Dictionary<Guid, (string Name, string Phone)>();
        var dtos = new List<PurchaseReviewDto>(items.Count);
        foreach (var r in items)
        {
            if (!consumerCache.TryGetValue(r.ConsumerAccountId, out var info))
            {
                var consumer = await _consumerAccounts.GetByIdAsync(r.ConsumerAccountId, ct);
                info = (consumer?.FullName ?? "—", consumer?.Phone ?? "—");
                consumerCache[r.ConsumerAccountId] = info;
            }
            dtos.Add(ToDto(r, info.Name, info.Phone));
        }

        return new PagedResult<PurchaseReviewDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
    }

    public async Task<(PurchaseReviewDto? Review, string? Error, int? StatusCode)> ReplyAsync(
        Guid tenantId, Guid reviewId, Guid staffUserId, string replyText, CancellationToken ct = default)
    {
        var trimmed = replyText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, ReplyRequiredError, 400);
        if (trimmed.Length > MaxReplyTextLength)
            return (null, $"Reply cannot exceed {MaxReplyTextLength} characters.", 400);

        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null || review.TenantId != tenantId)
            return (null, ReviewNotFoundError, 404); // uniform 404 — never discloses another tenant's review exists

        if (!string.IsNullOrEmpty(review.ReplyText))
            return (null, AlreadyRepliedError, 409);

        review.ReplyText = trimmed;
        review.RepliedAt = DateTimeOffset.UtcNow;
        review.RepliedByUserId = staffUserId;

        _reviews.Update(review);
        await _reviews.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Staff user {UserId} replied on purchase review {ReviewId} (tenant {TenantId}).",
            staffUserId, review.Id, tenantId);

        var consumer = await _consumerAccounts.GetByIdAsync(review.ConsumerAccountId, ct);
        return (ToDto(review, consumer?.FullName ?? "—", consumer?.Phone ?? "—"), null, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves whether <paramref name="posTransactionId"/> is actually
    /// <paramref name="consumerAccountId"/>'s own purchase at <paramref name="tenantId"/>.
    /// <c>PosTransaction</c> carries no direct ConsumerAccountId FK (only the tenant CRM
    /// CustomerId) — the join used here (<c>LoyaltyLedgerEntry.PosTransactionId → MembershipId
    /// → LoyaltyMembership.ConsumerAccountId</c>) is the cheapest, most direct one already
    /// established in this codebase: <c>ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync</c>
    /// (added for PosService.GetSalesForShiftAsync, TASK-410) documents this as literally "the
    /// only persisted signal that loyalty activity happened on that sale" — there is no cheaper
    /// path. A transaction with zero matching ledger entries (walk-in, never enrolled in
    /// loyalty) returns false — an accepted limitation per the approved plan.
    ///
    /// RLS is a second, independent layer here: <c>loyalty_ledger_entries</c>' own
    /// <c>consumer_self_access</c> policy (TASK-404) already makes another consumer's ledger
    /// rows invisible to this session, so the explicit <c>membership.ConsumerAccountId ==
    /// consumerAccountId</c> check below is belt-and-suspenders, not the only guard.
    /// </summary>
    private async Task<bool> IsOwnPurchaseAsync(
        Guid consumerAccountId, Guid tenantId, Guid posTransactionId, CancellationToken ct)
    {
        var ledgerEntries = await _loyalty.GetLedgerEntriesForTransactionsAsync(
            tenantId, new[] { posTransactionId }, ct);
        if (ledgerEntries.Count == 0)
            return false;

        var membershipId = ledgerEntries[0].MembershipId;
        var membership = await _loyalty.GetMembershipByIdAsync(membershipId, tenantId, ct);
        return membership is not null && membership.ConsumerAccountId == consumerAccountId;
    }

    private static PurchaseReviewDto ToDto(PurchaseReview r, string consumerName, string consumerPhone) => new(
        r.Id, r.TenantId, r.ConsumerAccountId, consumerName, consumerPhone, r.PosTransactionId,
        r.Rating, r.Comment, r.CreatedAt, r.ReplyText, r.RepliedAt, r.RepliedByUserId);
}
