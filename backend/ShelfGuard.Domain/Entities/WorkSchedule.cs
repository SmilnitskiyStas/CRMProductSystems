namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Weekly schedule template/plan for a specific location.
/// Maps to "work_schedules" table.
/// </summary>
public sealed class WorkSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LocationId { get; set; }

    /// <summary>Human-readable schedule name, e.g. "Main Schedule", "Summer Schedule".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Monday of the week this schedule covers (for weekly view).</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>Lifecycle status: draft | published | archived.</summary>
    public string Status { get; set; } = "draft";

    /// <summary>User who created this schedule. Nullable — may be null if user was deleted.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Location? Location { get; init; }
    public User? CreatedByUser { get; init; }
    public ICollection<ScheduleShift> Shifts { get; init; } = new List<ScheduleShift>();
}
