using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PurchaseReviewRepository : IPurchaseReviewRepository
{
    private const string PosTransactionUniqueIndexName = "uq_purchase_reviews_pos_transaction";

    private readonly AppDbContext _db;

    public PurchaseReviewRepository(AppDbContext db) => _db = db;

    public Task<PurchaseReview?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PurchaseReviews.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<PurchaseReview?> GetByTransactionAsync(Guid posTransactionId, CancellationToken ct = default) =>
        _db.PurchaseReviews.FirstOrDefaultAsync(r => r.PosTransactionId == posTransactionId, ct);

    public Task<List<PurchaseReview>> GetRecentForCustomerAsync(
        Guid customerId, Guid tenantId, int take, CancellationToken ct = default) =>
        // PurchaseReview carries no CustomerId of its own — explicit join on the scalar FK
        // through the linked PosTransaction's CustomerId (see interface doc for why this
        // differs from ReviewService's own ledger-based ownership check).
        (from r in _db.PurchaseReviews.AsNoTracking()
         join t in _db.PosTransactions.AsNoTracking() on r.PosTransactionId equals t.Id
         where r.TenantId == tenantId && t.CustomerId == customerId
         orderby r.CreatedAt descending
         select r)
        .Take(take)
        .ToListAsync(ct);

    public async Task<(List<PurchaseReview> Items, int Total)> GetPagedForConsumerAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.PurchaseReviews
            .Where(r => r.ConsumerAccountId == consumerAccountId && r.TenantId == tenantId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(List<PurchaseReview> Items, int Total)> GetPagedForTenantAsync(
        Guid tenantId, short? ratingFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.PurchaseReviews.Where(r => r.TenantId == tenantId);
        if (ratingFilter is short rating)
            query = query.Where(r => r.Rating == rating);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task AddAsync(PurchaseReview review, CancellationToken ct = default) =>
        _db.PurchaseReviews.AddAsync(review, ct).AsTask();

    public void Update(PurchaseReview review) => _db.PurchaseReviews.Update(review);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsPosTransactionUniqueViolation(ex))
        {
            // TASK-617: ReviewService.CreateReviewAsync already pre-checks GetByTransactionAsync
            // before inserting — this only fires on a genuine race (two requests for the same
            // PosTransactionId landing concurrently), same "pre-check + DB backstop" shape as
            // LoyaltyRepository's ConcurrencyConflictException translation above it.
            throw new DuplicateReviewException(
                "A review for this purchase already exists.", ex);
        }
    }

    private static bool IsPosTransactionUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
        string.Equals(pg.ConstraintName, PosTransactionUniqueIndexName, StringComparison.Ordinal);
}
