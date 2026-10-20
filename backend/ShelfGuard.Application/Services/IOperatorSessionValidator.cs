namespace ShelfGuard.Application.Services;

/// <summary>
/// Server-side liveness check for cashier-app operator JWTs (carry a <c>session_id</c> claim).
/// A token is only honoured while its register_sessions row is open, the register is 'active',
/// the user is active and still has a POS role, and the tenant has the 'pos' module. Results are
/// cached briefly; <see cref="IOperatorSessionCache.Invalidate"/> drops an entry immediately.
/// </summary>
public interface IOperatorSessionValidator
{
    Task<bool> IsValidAsync(
        Guid tenantId, Guid sessionId, Guid registerId, Guid userId, CancellationToken ct = default);
}

public interface IOperatorSessionCache
{
    /// <summary>Call when a session ends / is replaced / its register is revoked.</summary>
    void Invalidate(Guid sessionId);
}
