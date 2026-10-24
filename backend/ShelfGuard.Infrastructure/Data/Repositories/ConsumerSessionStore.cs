using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ConsumerSessionStore(AppDbContext db) : IConsumerSessionStore
{
    public async Task<bool> IssueAsync(ConsumerAccount account, ConsumerRefreshToken token, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock the owner before inserting a session; serialize with revoke-all/password/deactivation.
        var changed = await db.ConsumerAccounts.Where(a => a.Id == account.Id && a.IsActive
            && a.AuthVersion == token.AuthVersion && a.PasswordHash == account.PasswordHash)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.LastLoginAt, DateTimeOffset.UtcNow), ct);
        if (changed != 1) return false;
        db.ConsumerRefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public Task<ConsumerRefreshToken?> FindAsync(string tokenHash, CancellationToken ct = default) =>
        db.ConsumerRefreshTokens.AsNoTracking().Include(t => t.ConsumerAccount).SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<bool> RotateAsync(ConsumerRefreshToken existing, ConsumerRefreshToken next, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var owner = await db.ConsumerAccounts.Where(a => a.Id == existing.ConsumerAccountId && a.IsActive && a.AuthVersion == existing.AuthVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.AuthVersion, a => a.AuthVersion), ct);
        if (owner != 1) return false;
        var changed = await db.ConsumerRefreshTokens.Where(t => t.Id == existing.Id && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, DateTime.UtcNow)
                .SetProperty(t => t.ReplacedByTokenHash, next.TokenHash), ct);
        if (changed != 1) return false;
        db.ConsumerRefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RevokeAsync(Guid accountId, Guid? sessionId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Owner lock and session UPDATE serialize with both issuance and rotation.
        await db.ConsumerAccounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(update =>
            update.SetProperty(a => a.AuthVersion, a => sessionId == null ? a.AuthVersion + 1 : a.AuthVersion), ct);
        await db.ConsumerRefreshTokens.Where(t => t.ConsumerAccountId == accountId && (sessionId == null || t.SessionId == sessionId) && t.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct);
    }
}
