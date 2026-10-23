using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>Each mutation and its outbox/audit/session effects must commit atomically.</summary>
public interface IPasswordRecoveryStore
{
    Task<bool> IssueAsync(User user, string tokenHash, DateTime now, DateTime expiresAt,
        NotificationQueue notification, CancellationToken ct = default);
    Task<bool> ConfirmAsync(Guid userId, string tokenHash, string expectedPasswordHash,
        string newPasswordHash, DateTime now, string? ipAddress, CancellationToken ct = default);
}
