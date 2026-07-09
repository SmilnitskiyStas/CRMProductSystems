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
    /// <summary>Optional legal entity this user is registered under (TASK-321).</summary>
    public Guid? LegalEntityId { get; private set; }
    public string? TelegramChatId { get; private set; }
    public string? PushToken { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime? LastActiveAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Per-user page-access overrides on top of role defaults.
    /// Key = page slug (e.g. "analytics"), Value = true (grant) / false (deny).
    /// Null / missing key → use role default.
    /// Stored as jsonb in PostgreSQL.
    /// </summary>
    public Dictionary<string, bool>? Permissions { get; private set; }

    /// <summary>
    /// Custom provider role assigned to this user (provider team only).
    /// Null means the user's base system role applies.
    /// </summary>
    public Guid? ProviderRoleId { get; private set; }

    /// <summary>
    /// Custom supplier role assigned to this user (supplier cabinet staff only).
    /// Null means the user's base system role applies (e.g. full access for the
    /// supplier owner/admin).
    /// </summary>
    public Guid? SupplierRoleId { get; private set; }

    /// <summary>
    /// Display name of the user who created/invited this account.
    /// Null for seed/self-registered users.
    /// Denormalized for fast read — not a FK to avoid cascades.
    /// </summary>
    public string? InvitedByName { get; private set; }

    public Tenant? Tenant { get; private set; }
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    private User() { }

    public static User Create(
        Guid? tenantId,
        string email,
        string fullName,
        string passwordHash,
        string role,
        Guid? storeId = null,
        string? invitedByName = null,
        Guid? legalEntityId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email.ToLowerInvariant(),
        FullName = fullName,
        PasswordHash = passwordHash,
        Role = role,
        StoreId = storeId,
        LegalEntityId = legalEntityId,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        InvitedByName = invitedByName,
    };

    public void UpdateLastActive() => LastActiveAt = DateTime.UtcNow;

    public void UpdatePushToken(string? token) => PushToken = token;

    public void LinkTelegram(string chatId) => TelegramChatId = chatId;

    public void UpdateProfile(string fullName, string? phone)
    {
        FullName = fullName;
        Phone    = phone;
    }

    public void ChangePassword(string newHash) => PasswordHash = newHash;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetRole(string role) => Role = role;

    public void SetProviderRole(Guid? roleId) => ProviderRoleId = roleId;

    public void SetSupplierRole(Guid? roleId) => SupplierRoleId = roleId;

    public void SetStore(Guid? storeId) => StoreId = storeId;

    public void SetLegalEntity(Guid? legalEntityId) => LegalEntityId = legalEntityId;

    /// <summary>
    /// Replaces per-user page-access overrides.
    /// Pass null to clear all overrides (revert to role defaults).
    /// </summary>
    public void SetPermissions(Dictionary<string, bool>? permissions) => Permissions = permissions;
}
