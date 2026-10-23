namespace ShelfGuard.Domain.Entities;

/// <summary>Hashed rotating refresh proof for one independent personal device session.</summary>
public sealed class ConsumerRefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ConsumerAccountId { get; init; }
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public string TokenHash { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public int AuthVersion { get; init; }
    public ConsumerAccount ConsumerAccount { get; init; } = null!;
}
