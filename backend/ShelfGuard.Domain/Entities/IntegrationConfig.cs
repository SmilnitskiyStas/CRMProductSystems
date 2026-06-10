namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Stores per-tenant credentials and settings for an external service integration.
/// Config JSONB is encrypted at the Application layer before persistence.
/// Supported services: telegram, resend, webhook, prro, iot
/// </summary>
public sealed class IntegrationConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Tenant this integration belongs to.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Service identifier, e.g. "telegram", "resend", "webhook", "prro", "iot".</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>JSON credentials/settings blob. Sensitive fields encrypted before storage.</summary>
    public string Config { get; set; } = "{}";

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant? Tenant { get; init; }
}
