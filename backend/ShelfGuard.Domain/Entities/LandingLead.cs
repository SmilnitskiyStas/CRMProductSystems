namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Lead captured from the public landing page (TASK-333, extended TASK-721).
/// Maps to "landing_leads" table. No tenant_id — provider-level data,
/// same pattern as provider_roles / provider_schedule_slots (no RLS).
/// IP addresses are intentionally never stored.
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

    public string? PageUrl { get; private set; }
    public string? Locale { get; private set; }
    public string? Referrer { get; private set; }
    public string? UtmSource { get; private set; }
    public string? UtmMedium { get; private set; }
    public string? UtmCampaign { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public Guid? ProcessedByUserId { get; private set; }
    public string? AdminNote { get; private set; }

    /// <summary>Set at capture time by LeadCriticalityEvaluator.</summary>
    public bool IsCritical { get; private set; }
    /// <summary>Short reason code(s), e.g. "network+urgent+repeat". Max 50 chars.</summary>
    public string? CriticalReason { get; private set; }

    /// <summary>Request kind for source="beauty": cooperation | improvement | bug. Null for landing forms.</summary>
    public string? RequestType { get; private set; }
    /// <summary>Reference to the Beauty CRM business (ShelfGuard tenant id sent as externalRef). Null for landing forms.</summary>
    public string? BeautyTenantRef { get; private set; }
    /// <summary>Slug of the Beauty business when a ShelfGuard link is known at ingest time.</summary>
    public string? BeautyTenantSlug { get; private set; }
    /// <summary>Role of the Beauty user who sent the request (owner | admin | specialist).</summary>
    public string? ReporterRole { get; private set; }
    /// <summary>Free-form contact of the reporter (may be an email); full value, unlike Phone.</summary>
    public string? Contact { get; private set; }
    /// <summary>Idempotency key from the source system (Beauty feedbackId). Unique when set.</summary>
    public string? ExternalId { get; private set; }

    private LandingLead() { }

    public static LandingLead Create(
        string name, string phone, string? company, string? message, string source = "landing",
        string? pageUrl = null, string? locale = null, string? referrer = null,
        string? utmSource = null, string? utmMedium = null, string? utmCampaign = null,
        bool isCritical = false, string? criticalReason = null,
        string? requestType = null, string? beautyTenantRef = null, string? reporterRole = null,
        string? contact = null, string? externalId = null, string? beautyTenantSlug = null) => new()
    {
        Id          = Guid.NewGuid(),
        Name        = name,
        Phone       = phone,
        Company     = company,
        Message     = message,
        Source      = source,
        IsProcessed = false,
        CreatedAt   = DateTime.UtcNow,
        PageUrl     = pageUrl,
        Locale      = locale,
        Referrer    = referrer,
        UtmSource   = utmSource,
        UtmMedium   = utmMedium,
        UtmCampaign = utmCampaign,
        IsCritical  = isCritical,
        CriticalReason = criticalReason,
        RequestType = requestType,
        BeautyTenantRef = beautyTenantRef,
        ReporterRole = reporterRole,
        Contact = contact,
        ExternalId = externalId,
        BeautyTenantSlug = beautyTenantSlug,
    };

    public void MarkProcessed(Guid? userId)
    {
        IsProcessed       = true;
        ProcessedAt       = DateTime.UtcNow;
        ProcessedByUserId = userId;
    }

    public void MarkUnprocessed()
    {
        IsProcessed       = false;
        ProcessedAt       = null;
        ProcessedByUserId = null;
    }

    public void SetAdminNote(string? note) => AdminNote = note;
}
