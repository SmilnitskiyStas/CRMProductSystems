namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Append-only audit trail of profile field edits on a <see cref="ConsumerAccount"/>
/// (TASK-613), written when a consumer self-edits their name/phone/email via
/// <c>Features/ConsumerProfile</c>. Deliberately NOT tenant-scoped and carries NO Row
/// Level Security at all — same precedent as <see cref="ConsumerAccount"/> itself
/// (globally readable, protected only by application code never handing a generic
/// GetById to a non-owner; see the AddLoyaltyProgram migration for the full rationale).
/// One row per changed field per edit — changing phone and email together produces two
/// rows, not one. Every property is <c>init</c>-only: rows are never updated or deleted.
/// </summary>
public sealed class ConsumerAccountProfileChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ConsumerAccountId { get; init; }

    /// <summary>See <see cref="ConsumerAccountProfileChangeField"/>.</summary>
    public string FieldName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;

    public ConsumerAccount? ConsumerAccount { get; init; }
}

/// <summary>Valid <see cref="ConsumerAccountProfileChange.FieldName"/> values.</summary>
public static class ConsumerAccountProfileChangeField
{
    public const string Phone    = "phone";
    public const string Email    = "email";
    public const string FullName = "full_name";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Phone, Email, FullName };
}
