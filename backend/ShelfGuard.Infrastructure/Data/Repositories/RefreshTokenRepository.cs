using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) =>
        _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow, ct);

    public async Task<bool> ConfirmMfaAsync(User user, Guid sessionId, DateTime verifiedAt, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var owner = await _db.Users.Where(u => u.Id == user.Id && u.IsActive && u.TotpEnabled && u.AuthVersion == user.AuthVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.AuthVersion, u => u.AuthVersion), ct);
        if (owner != 1) return false;
        // The caller loads a tracked user in this scope; keep its original concurrency
        // snapshot and write only the changed MFA/lockout fields, never stale permissions.
        if (_db.Entry(user).State == EntityState.Detached) throw new InvalidOperationException("MFA requires a tracked user.");
        _db.ActivityLogs.Add(new ActivityLog { TenantId = user.TenantId, UserId = user.Id, Action = "user.mfa_step_up",
            EntityType = "session", EntityId = sessionId, CreatedAt = verifiedAt });
        await _db.SaveChangesAsync(ct);
        var changed = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.SessionId == sessionId && t.AuthVersion == user.AuthVersion && t.RevokedAt == null && t.ExpiresAt > verifiedAt)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.MfaVerifiedAt, verifiedAt), ct);
        if (changed != 1) return false;
        await tx.CommitAsync(ct); return true;
    }
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public Task<RefreshToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.RevokedAt == null &&
                t.ExpiresAt > DateTime.UtcNow, ct);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public void Update(RefreshToken token) => _db.RefreshTokens.Update(token);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var token in active)
            token.Revoke();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    public async Task<bool> TryRotateAsync(RefreshToken existing, RefreshToken next, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var owner = await _db.Users.Where(u => u.Id == existing.UserId && u.IsActive && u.AuthVersion == existing.AuthVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.AuthVersion, u => u.AuthVersion), ct);
        if (owner != 1) return false;
        var changed = await _db.RefreshTokens.Where(t => t.Id == existing.Id && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow && t.MfaVerifiedAt == existing.MfaVerifiedAt)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, DateTime.UtcNow)
                .SetProperty(t => t.ReplacedByTokenHash, next.TokenHash), ct);
        if (changed != 1) return false;
        _db.RefreshTokens.Add(next);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(update => update.SetProperty(u => u.AuthVersion, u => u.AuthVersion), ct);
        await _db.RefreshTokens.Where(t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct);
    }

    public async Task RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(update => update.SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1), ct);
        await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct);
    }
}
