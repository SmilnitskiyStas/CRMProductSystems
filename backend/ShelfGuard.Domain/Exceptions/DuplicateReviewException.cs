namespace ShelfGuard.Domain.Exceptions;

/// <summary>
/// Thrown by <c>IPurchaseReviewRepository.SaveChangesAsync</c> (TASK-617) when Postgres rejects
/// an insert against <c>uq_purchase_reviews_pos_transaction</c> — the unique index backing "one
/// review per purchase" (see <see cref="Entities.PurchaseReview"/>'s class doc). ReviewService's
/// own pre-check (<c>GetByTransactionAsync</c>) catches this in the common case; this exception
/// is the DB-level backstop for the genuine race — two requests for the same PosTransactionId
/// landing concurrently. Infrastructure translates the EF Core/Npgsql-specific exception into
/// this Domain-level type so Application services can catch it without depending on EF Core
/// (architecture rule: EF Core stays in Infrastructure) — same pattern as
/// <see cref="ConcurrencyConflictException"/>.
/// </summary>
public sealed class DuplicateReviewException : Exception
{
    public DuplicateReviewException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
