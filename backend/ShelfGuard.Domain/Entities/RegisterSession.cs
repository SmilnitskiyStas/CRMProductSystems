namespace ShelfGuard.Domain.Entities;

/// <summary>Who signed in on which register and when (operator session audit trail).</summary>
public sealed class RegisterSession
{
    public const string EndReasonLogout = "logout";
    public const string EndReasonIdle = "idle";
    public const string EndReasonRevoked = "revoked";
    public const string EndReasonReplaced = "replaced";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RegisterId { get; init; }
    public Guid CashierId { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    public CashRegister? Register { get; init; }
    public User? Cashier { get; init; }
}
