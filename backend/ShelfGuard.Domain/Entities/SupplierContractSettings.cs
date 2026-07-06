namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Contract requisites of a supplier tenant (TASK-316) — legal data, payment
/// details, and signature/stamp images used when generating the cooperation
/// contract PDF. One row per supplier tenant (TenantId is UNIQUE).
/// </summary>
public sealed class SupplierContractSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>The supplier tenant these requisites belong to. UNIQUE.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Official legal name (e.g. ТОВ «Постачальник»).</summary>
    public string LegalName { get; set; } = string.Empty;
    /// <summary>ЄДРПОУ / ІПН code.</summary>
    public string? Edrpou { get; set; }
    public string? Iban { get; set; }
    public string? BankName { get; set; }
    public string? LegalAddress { get; set; }
    public string? DirectorName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Subject of the contract — what the supplier delivers.</summary>
    public string? ServiceName { get; set; }
    public string? ServiceDescription { get; set; }
    public string? SignatureImageUrl { get; set; }
    public string? StampImageUrl { get; set; }
    public bool IsVatPayer { get; set; } = false;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
