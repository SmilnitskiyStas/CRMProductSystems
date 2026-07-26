namespace ShelfGuard.Application.Services;

/// <summary>
/// Tracks failed loyalty resolve-code attempts per membership (TASK-405, Loyalty Фаза 0) —
/// the same N-failures-then-lockout SHAPE as User.FailedLoginAttempts/LockoutUntil
/// (TASK-329), applied to POST /api/loyalty/resolve-code. LoyaltyMembership carries no such
/// columns (schema frozen for this task, see task log) — this abstraction lets the
/// Application-layer LoyaltyService depend on the behavior without depending on a specific
/// storage technology; the Infrastructure implementation is in-process (IMemoryCache), which
/// is a single-instance-deployment tradeoff explicitly called out for security-reviewer (a
/// future multi-instance deployment would need a shared store, e.g. Redis, already used by
/// the BullMQ worker).
/// </summary>
public interface IResolveCodeAttemptTracker
{
    /// <summary>True while this membership is currently locked out.</summary>
    bool IsLockedOut(Guid membershipId);

    /// <summary>
    /// Records a failed attempt; starts a lockout once <paramref name="maxAttempts"/> is
    /// reached. Returns true when this specific call is the one that triggered the lockout
    /// (for audit-log branching), mirroring User.RegisterFailedLogin's return convention.
    /// </summary>
    bool RegisterFailure(Guid membershipId, int maxAttempts, TimeSpan lockoutDuration);

    /// <summary>Clears any failure count/lockout for this membership (successful resolve).</summary>
    void Reset(Guid membershipId);
}
