namespace ShelfGuard.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public Guid? StoreId { get; private set; }
    public string? TelegramChatId { get; private set; }
    public string? PushToken { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime? LastActiveAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Tenant? Tenant { get; private set; }
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    private User() { }

    public static User Create(
        Guid? tenantId,
        string email,
        string fullName,
        string passwordHash,
        string role,
        Guid? storeId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email.ToLowerInvariant(),
        FullName = fullName,
        PasswordHash = passwordHash,
        Role = role,
        StoreId = storeId,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    public void UpdateLastActive() => LastActiveAt = DateTime.UtcNow;

    public void UpdatePushToken(string? token) => PushToken = token;

    public void LinkTelegram(string chatId) => TelegramChatId = chatId;
}
