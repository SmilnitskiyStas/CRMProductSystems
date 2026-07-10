using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Fetches a token by hash regardless of revocation/expiry state.
    /// Used by refresh-reuse (theft) detection — a presented token that exists
    /// but is already revoked means the cookie was stolen and replayed (TASK-329).
    /// </summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    void Update(RefreshToken token);

    /// <summary>
    /// Revokes every active refresh token of a user (password change,
    /// refresh-token reuse detection). Does NOT call SaveChanges.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
