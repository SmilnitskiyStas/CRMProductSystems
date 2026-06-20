namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Recurring weekly availability slot for a provider team member.
/// Maps to "provider_schedule_slots" table. No tenant_id — provider team is platform-global.
/// </summary>
public sealed class ProviderScheduleSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>FK → users (must have a provider* role).</summary>
    public Guid UserId { get; init; }

    /// <summary>0 = Monday, 1 = Tuesday, … 6 = Sunday.</summary>
    public byte DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Navigation
    public User? User { get; init; }
}
