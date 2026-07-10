namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Lead captured from the public landing page (TASK-333).
/// Maps to "landing_leads" table. No tenant_id — provider-level data,
/// same pattern as provider_roles / provider_schedule_slots (no RLS).
/// </summary>
public sealed class LandingLead
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Company { get; private set; }
    public string? Message { get; private set; }
    public string Source { get; private set; } = "landing";
    public bool IsProcessed { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private LandingLead() { }

    public static LandingLead Create(
        string name, string phone, string? company, string? message, string source = "landing") => new()
    {
        Id          = Guid.NewGuid(),
        Name        = name,
        Phone       = phone,
        Company     = company,
        Message     = message,
        Source      = source,
        IsProcessed = false,
        CreatedAt   = DateTime.UtcNow,
    };

    public void MarkProcessed() => IsProcessed = true;
}
