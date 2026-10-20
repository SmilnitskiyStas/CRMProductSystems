namespace ShelfGuard.Domain.Entities;

/// <summary>
/// One AI check of a shelf photo against the zone's planogram (<see cref="ShelfItem"/>).
/// <see cref="ResultJson"/> holds the per-item verdicts. <see cref="AlertPending"/> is raised
/// when the result has missing/low items; the worker picks such rows up, sends the
/// <c>shelf.empty</c> notification and clears the flag.
/// </summary>
public sealed class ShelfScan
{
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LocationId { get; init; }
    public Guid ZoneId { get; init; }
    public Guid? ShelfId { get; init; }
    public string ImagePath { get; init; } = string.Empty;
    public string Status { get; set; } = StatusCompleted;
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public int MissingCount { get; set; }
    public int LowCount { get; set; }
    public bool AlertPending { get; set; }
    public string? Model { get; set; }
    public int TokensUsed { get; set; }
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
