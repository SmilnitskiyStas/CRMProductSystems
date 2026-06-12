namespace ShelfGuard.Domain.Entities;

/// <summary>
/// One-time code for binding a Telegram chat to a user (v1-spec §8.1).
/// User requests a code in the app → opens t.me/&lt;bot&gt;?start=&lt;code&gt; →
/// the worker's bot listener validates it and writes users.TelegramChatId.
/// </summary>
public sealed class TelegramLinkCode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public User? User { get; init; }
}
