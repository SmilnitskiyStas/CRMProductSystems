namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single shift assigned to a specific employee within a work schedule.
/// Maps to "schedule_shifts" table.
/// </summary>
public sealed class ScheduleShift
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ScheduleId { get; set; }
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }

    public DateOnly ShiftDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>Break duration in minutes. Default 60.</summary>
    public int BreakMinutes { get; set; } = 60;

    /// <summary>Optional role override if the employee works in a different role for this shift.</summary>
    public string? RoleOverride { get; set; }

    public string? Notes { get; set; }

    /// <summary>Lifecycle status: scheduled | confirmed | completed | cancelled.</summary>
    public string Status { get; set; } = "scheduled";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Navigation
    public WorkSchedule? Schedule { get; init; }
    public User? User { get; init; }
    public Location? Location { get; init; }
}
