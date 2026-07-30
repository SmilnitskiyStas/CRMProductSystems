namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Single-use, time-boxed token for the forgot/reset-password flow (TASK-455).
/// Same "single active token per user" shape as <see cref="TelegramLinkCode"/>, but styled
/// like <see cref="RefreshToken"/> (private setters, <see cref="Create"/> factory, computed
/// <see cref="IsActive"/>, <see cref="MarkUsed"/>) rather than TelegramLinkCode's anemic style.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsActive => UsedAt is null && DateTime.UtcNow < ExpiresAt;

    public User? User { get; private set; }

    private PasswordResetToken() { }

    public static PasswordResetToken Create(Guid userId, string tokenHash, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow,
    };

    public void MarkUsed()
    {
        UsedAt = DateTime.UtcNow;
    }
}
