using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for <see cref="PurchaseReview"/> (TASK-617) — a consumer's rating + optional
/// comment on a specific completed <see cref="PosTransaction"/>, plus one staff reply. Mirrors
/// <see cref="IConsumerSupportTicketRepository"/>'s shape: canonical tenant RLS triad +
/// direct-column <c>consumer_self_access</c> (see the AddPurchaseReviews migration, TASK-613).
/// All queries here run under whatever RLS context the caller's session already carries — no
/// <c>ITenantSessionOverride</c> is needed anywhere in this feature (see ReviewService's own
/// remarks for why).
/// </summary>
public interface IPurchaseReviewRepository
{
    /// <summary>Tracked lookup by primary key — callers may mutate the returned entity (e.g. a
    /// staff reply) and call <see cref="SaveChangesAsync"/> without a separate
    /// <see cref="Update"/> call, though <see cref="Update"/> is still called for clarity at
    /// call sites (mirrors ConsumerSupportTicketRepository's own convention).</summary>
    Task<PurchaseReview?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The one review (if any) already on file for this purchase — backs both the
    /// friendly pre-check (ReviewService.CreateReviewAsync returns a clean 409 before ever
    /// touching the database's own <c>uq_purchase_reviews_pos_transaction</c> unique index) and
    /// nothing else. PosTransactionId is globally unique (one row per sale, one tenant per
    /// sale via FK) so no separate tenantId parameter is needed here.
    /// </summary>
    Task<PurchaseReview?> GetByTransactionAsync(Guid posTransactionId, CancellationToken ct = default);

    /// <summary>This consumer's own reviews at one tenant, newest first, paged.</summary>
    Task<(List<PurchaseReview> Items, int Total)> GetPagedForConsumerAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// TASK-618: most recent reviews left on a CRM <see cref="Customer"/>'s own purchases —
    /// backs the Customers detail view. Joins through <see cref="PosTransaction.CustomerId"/>
    /// (PurchaseReview itself carries no CustomerId — see ReviewService's own remarks on why
    /// ownership is resolved via the loyalty ledger, not this column). Newest first, capped at
    /// <paramref name="take"/>.
    /// </summary>
    Task<List<PurchaseReview>> GetRecentForCustomerAsync(
        Guid customerId, Guid tenantId, int take, CancellationToken ct = default);

    /// <summary>Staff inbox: every review at a tenant, newest first, optionally filtered by
    /// <see cref="PurchaseReview.Rating"/>, paged.</summary>
    Task<(List<PurchaseReview> Items, int Total)> GetPagedForTenantAsync(
        Guid tenantId, short? ratingFilter, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(PurchaseReview review, CancellationToken ct = default);

    void Update(PurchaseReview review);

    /// <summary>
    /// Throws <see cref="Exceptions.DuplicateReviewException"/> (translated from the Npgsql
    /// unique-violation on <c>uq_purchase_reviews_pos_transaction</c>) if a concurrent request
    /// won the race to review the same PosTransactionId first — see that exception's doc.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
