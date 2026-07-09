namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Legal entity (юридична особа) registered under a tenant (TASK-321).
/// A tenant can have many legal entities (e.g. multiple ТОВ/ФОП running
/// different stores/staff under one business). Stores/Locations/Users may
/// optionally reference a LegalEntity to prepare the ground for a future
/// accounting module — no accounting logic lives here yet.
/// </summary>
public sealed class LegalEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Official legal name (e.g. ТОВ «Ромашка»).</summary>
    public string LegalName { get; set; } = string.Empty;
    /// <summary>ЄДРПОУ / ІПН code.</summary>
    public string? Edrpou { get; set; }
    public string? LegalAddress { get; set; }
    public string? DirectorName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Iban { get; set; }
    public string? BankName { get; set; }
    public bool IsVatPayer { get; set; } = false;

    /// <summary>Soft delete / archive flag.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
}
