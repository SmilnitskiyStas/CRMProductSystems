using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PasswordRecoveryStore(AppDbContext db) : IPasswordRecoveryStore
{
    public async Task<bool> IssueAsync(User user, string tokenHash, DateTime now, DateTime expiresAt,
        NotificationQueue notification, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cooldown = now.AddSeconds(-60);
        var changed = await db.Users.Where(u => u.Id == user.Id && u.IsActive
            && u.Email == user.Email && u.PasswordHash == user.PasswordHash
            && (u.PasswordResetRequestedAt == null || u.PasswordResetRequestedAt <= cooldown))
            .ExecuteUpdateAsync(update => update
                .SetProperty(u => u.PasswordResetTokenHash, tokenHash)
                .SetProperty(u => u.PasswordResetExpiresAt, expiresAt)
                .SetProperty(u => u.PasswordResetRequestedAt, now), ct);
        if (changed != 1) return false;
        db.NotificationQueues.Add(notification);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ConfirmAsync(Guid userId, string tokenHash, string expectedPasswordHash,
        string newPasswordHash, DateTime now, string? ipAddress, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tenant = await db.Users.Where(u => u.Id == userId).Select(u => new { u.TenantId }).SingleOrDefaultAsync(ct);
        if (tenant is null) return false;
        var changed = await db.Users.Where(u => u.Id == userId && u.IsActive
            && u.PasswordResetTokenHash == tokenHash && u.PasswordResetExpiresAt > now
            && u.PasswordHash == expectedPasswordHash)
            .ExecuteUpdateAsync(update => update
                .SetProperty(u => u.PasswordHash, newPasswordHash)
                .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)
                .SetProperty(u => u.PasswordResetTokenHash, (string?)null)
                .SetProperty(u => u.PasswordResetExpiresAt, (DateTime?)null)
                .SetProperty(u => u.TempPasswordExpiresAt, (DateTime?)null)
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.LockoutUntil, (DateTime?)null), ct);
        if (changed != 1) return false;
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, now), ct);
        db.ActivityLogs.Add(new ActivityLog { TenantId = tenant.TenantId, UserId = userId,
            Action = "user.password_reset_confirmed", EntityType = "user", EntityId = userId,
            Meta = "Password reset proof consumed; refresh sessions revoked.", IpAddress = ipAddress });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
