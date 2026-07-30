using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IPasswordResetTokenRepository
{
    /// <summary>Marks all unused, unexpired tokens of the user as used (single-active-token rule).</summary>
    Task InvalidateActiveTokensAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// TASK-460 (security review, TASK-458 MEDIUM finding): true if this user already requested a
    /// reset within <paramref name="window"/> — i.e. a token exists with <c>CreatedAt &gt; utcNow -
    /// window</c> and <c>UsedAt == null</c>, regardless of <c>ExpiresAt</c>. This checks "was one
    /// just requested", not "is one still usable" (that's <see cref="GetActiveByHashAsync"/>'s job) —
    /// it is the per-user cooldown backstop for <c>ForgotPasswordAsync</c>, independent of the
    /// per-IP rate limiter that KI-014 already confirms is ineffective in production.
    /// </summary>
    Task<bool> HasRecentActiveTokenAsync(Guid userId, TimeSpan window, CancellationToken ct = default);

    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);

    Task<PasswordResetToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
