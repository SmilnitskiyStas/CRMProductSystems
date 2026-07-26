namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Global consumer identity for the loyalty program (Фаза 0, `docs/uployal/` plan).
/// Deliberately NOT tenant-scoped and carries NO Row Level Security at all — same
/// precedent as <see cref="Tenant"/> (globally readable, protected only by application
/// code never handing a generic GetById to a non-owner; see the AddLoyaltyProgram
/// migration for the full rationale and the security-reviewer follow-up flag).
/// One ConsumerAccount backs exactly one JWT (claim <c>consumer_account_id</c>, no
/// <c>tenant_id</c>) and can hold a <see cref="LoyaltyMembership"/> in many tenants at
/// once — a "wallet of cards" without re-login per network.
/// </summary>
public sealed class ConsumerAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Normalized to +380XXXXXXXXX. Globally unique — unlike <see cref="Customer.Phone"/>,
    /// which is only unique within a single tenant.
    /// </summary>
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }

    // Account lockout — same shape as User.FailedLoginAttempts/LockoutUntil (TASK-329).
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<LoyaltyMembership> Memberships { get; init; } = new List<LoyaltyMembership>();
}
