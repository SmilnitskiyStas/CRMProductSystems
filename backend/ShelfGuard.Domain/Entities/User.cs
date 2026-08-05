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

    /// <summary>
    /// UI locale the user picked ("uk" / "en"). Null = no explicit choice —
    /// client falls back to the browser language (i18n rollout Block 1, TASK-375).
    /// </summary>
    public string? PreferredLocale { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime? LastActiveAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // ── Account lockout (TASK-329) ─────────────────────────────────────────
    /// <summary>Consecutive failed login attempts since the last success/lockout.</summary>
    public int FailedLoginAttempts { get; private set; }
    /// <summary>While set and in the future, logins are rejected with a generic error.</summary>
    public DateTime? LockoutUntil { get; private set; }

    // ── Temporary password (forgot-password flow, TASK-464/465) ─────────────
    /// <summary>
    /// While set and in the future, <see cref="PasswordHash"/> holds a temporary password
    /// issued by the forgot-password flow rather than one the user chose. Not a token/link —
    /// the temporary password itself becomes the user's real, immediately-usable password.
    /// Cleared once the user changes their password through the authenticated change-password
    /// flow. No background job expires it; callers check <see cref="HasActiveTempPassword"/>
    /// (e.g. at login) since a lazily-checked timestamp needs no separate cleanup process.
    /// </summary>
    public DateTime? TempPasswordExpiresAt { get; private set; }

    // ── 2FA TOTP (TASK-330) ────────────────────────────────────────────────
    /// <summary>Base32 TOTP secret. Non-null with TotpEnabled=false means "pending setup".</summary>
    public string? TotpSecret { get; private set; }
    public bool TotpEnabled { get; private set; }
    /// <summary>SHA256 hashes of unused recovery codes (jsonb).</summary>
    public List<string>? TotpRecoveryCodes { get; private set; }
    /// <summary>Last accepted TOTP timestep — codes with timestep &lt;= this are replays.</summary>
    public long? TotpLastTimestep { get; private set; }

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
    /// Custom capability-template role assigned to this user (ADR-020, TASK-345).
    /// Additive on top of <see cref="Role"/>/its rank — grants specific capabilities
    /// (e.g. HR, accountant) without changing hierarchy. Null means no template
    /// capabilities beyond whatever the base <see cref="Role"/> already grants.
    /// </summary>
    public Guid? TenantRoleId { get; private set; }

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

    /// <summary>Sets the preferred UI locale ("uk"/"en"); null = browser fallback. Value is validated at the Application boundary.</summary>
    public void SetPreferredLocale(string? locale) => PreferredLocale = locale;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetRole(string role) => Role = role;

    public void SetProviderRole(Guid? roleId) => ProviderRoleId = roleId;

    public void SetSupplierRole(Guid? roleId) => SupplierRoleId = roleId;

    public void SetTenantRole(Guid? roleId) => TenantRoleId = roleId;

    public void SetStore(Guid? storeId) => StoreId = storeId;

    public void SetLegalEntity(Guid? legalEntityId) => LegalEntityId = legalEntityId;

    /// <summary>
    /// Replaces per-user page-access overrides.
    /// Pass null to clear all overrides (revert to role defaults).
    /// </summary>
    public void SetPermissions(Dictionary<string, bool>? permissions) => Permissions = permissions;

    // ── Account lockout (TASK-329) ─────────────────────────────────────────

    /// <summary>True while the account is temporarily locked after repeated failures.</summary>
    public bool IsLockedOut => LockoutUntil.HasValue && LockoutUntil.Value > DateTime.UtcNow;

    /// <summary>
    /// Records a failed login/2FA attempt. When <paramref name="maxAttempts"/> is
    /// reached the account is locked for <paramref name="lockoutDuration"/> and the
    /// counter resets. Returns true when this call triggered the lockout.
    /// </summary>
    public bool RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts < maxAttempts) return false;

        LockoutUntil = DateTime.UtcNow.Add(lockoutDuration);
        FailedLoginAttempts = 0;
        return true;
    }

    /// <summary>Clears the failure counter and any active lockout (successful auth).</summary>
    public void ResetLockout()
    {
        FailedLoginAttempts = 0;
        LockoutUntil = null;
    }

    // ── Temporary password (forgot-password flow, TASK-464/465) ─────────────

    /// <summary>True while <see cref="PasswordHash"/> still holds an unexpired temporary password.</summary>
    public bool HasActiveTempPassword =>
        TempPasswordExpiresAt.HasValue && TempPasswordExpiresAt.Value > DateTime.UtcNow;

    /// <summary>
    /// Marks the current <see cref="PasswordHash"/> as a temporary password valid until
    /// <paramref name="expiresAt"/>. Caller is responsible for having already set
    /// <see cref="PasswordHash"/> (via <see cref="ChangePassword"/>) to the temp password's hash.
    /// </summary>
    public void SetTempPasswordExpiry(DateTime expiresAt) => TempPasswordExpiresAt = expiresAt;

    /// <summary>Clears the temporary-password marker (e.g. once the user sets their own password).</summary>
    public void ClearTempPasswordExpiry() => TempPasswordExpiresAt = null;

    // ── 2FA TOTP (TASK-330) ────────────────────────────────────────────────

    /// <summary>Stores a freshly generated secret as "pending" — not enabled yet.</summary>
    public void SetPendingTotpSecret(string base32Secret)
    {
        TotpSecret = base32Secret;
        TotpEnabled = false;
        TotpRecoveryCodes = null;
        TotpLastTimestep = null;
    }

    /// <summary>Activates 2FA using the pending secret and stores recovery-code hashes.</summary>
    public void EnableTotp(List<string> recoveryCodeHashes)
    {
        TotpEnabled = true;
        TotpRecoveryCodes = recoveryCodeHashes;
    }

    /// <summary>Fully clears 2FA state (secret, recovery codes, replay marker).</summary>
    public void DisableTotp()
    {
        TotpSecret = null;
        TotpEnabled = false;
        TotpRecoveryCodes = null;
        TotpLastTimestep = null;
    }

    /// <summary>Anti-replay: remembers the highest accepted TOTP timestep.</summary>
    public void MarkTotpTimestepUsed(long timestep) => TotpLastTimestep = timestep;

    /// <summary>
    /// Removes a recovery-code hash if present (single use).
    /// Returns false when the hash is unknown or already consumed.
    /// </summary>
    public bool TryConsumeRecoveryCode(string codeHash)
    {
        if (TotpRecoveryCodes is null) return false;

        var index = TotpRecoveryCodes.FindIndex(h =>
            string.Equals(h, codeHash, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        // New list instance so the EF value comparer sees the change.
        var remaining = new List<string>(TotpRecoveryCodes);
        remaining.RemoveAt(index);
        TotpRecoveryCodes = remaining;
        return true;
    }
}
