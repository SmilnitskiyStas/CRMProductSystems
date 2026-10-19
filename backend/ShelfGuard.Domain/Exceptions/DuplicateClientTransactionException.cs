namespace ShelfGuard.Domain.Exceptions;

/// <summary>
/// Thrown by <c>IPosRepository.SaveChangesAsync</c> when Postgres rejects a sale insert against
/// the unique (TenantId, ClientTransactionId) index — i.e. the same offline sale was submitted
/// twice concurrently. Infrastructure translates the Npgsql unique violation so Application can
/// react (re-read and return the stored sale) without referencing EF Core.
/// </summary>
public sealed class DuplicateClientTransactionException : Exception
{
    public DuplicateClientTransactionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
