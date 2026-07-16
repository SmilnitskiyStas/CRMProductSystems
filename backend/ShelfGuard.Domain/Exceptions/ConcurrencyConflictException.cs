namespace ShelfGuard.Domain.Exceptions;

/// <summary>
/// Thrown by a repository's SaveChangesAsync when an optimistic-concurrency check
/// (Postgres xmin token) detects that a row was modified by another writer between
/// read and write — e.g. two concurrent POS sales decrementing the same stock batch.
/// Infrastructure translates the EF Core-specific DbUpdateConcurrencyException into
/// this Domain-level type so Application services can catch it without depending on
/// EF Core (architecture rule: EF Core stays in Infrastructure).
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
