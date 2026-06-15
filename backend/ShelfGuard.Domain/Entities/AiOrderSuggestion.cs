namespace ShelfGuard.Domain.Entities;

/// <summary>AI-generated order proposal for a store (v2-spec §7-8). One per generation run.</summary>
public sealed class AiOrderSuggestion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public DateOnly OrderDate { get; init; }
    /// <summary>Full context passed to the AI (weather, events, promos, stock) — audit trail.</summary>
    public string? ContextSnapshot { get; set; }
    /// <summary>pending / partially_accepted / accepted / rejected</summary>
    public string Status { get; set; } = "pending";
    public Guid? AcceptedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string AiModel { get; set; } = string.Empty;
    public int? TokensUsed { get; set; }

    public Location? Store { get; init; }
    public ICollection<AiOrderSuggestionItem> Items { get; init; } = new List<AiOrderSuggestionItem>();
}

public sealed class AiOrderSuggestionItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SuggestionId { get; init; }
    public Guid ProductId { get; init; }
    /// <summary>Mathematical formula result (before AI).</summary>
    public decimal QuantityBase { get; set; }
    /// <summary>AI-adjusted quantity.</summary>
    public decimal QuantitySuggested { get; set; }
    /// <summary>After manager edits; defaults to the AI suggestion.</summary>
    public decimal QuantityFinal { get; set; }
    public string? Reasoning { get; set; }
    /// <summary>high / medium / low</summary>
    public string? Confidence { get; set; }
    /// <summary>JSON: {"weather": 1.4, "event": 1.0, "promo": 0.8, "base": 80}</summary>
    public string? Factors { get; set; }
    public bool WasEdited { get; set; }
    public string? EditReason { get; set; }

    public AiOrderSuggestion? Suggestion { get; init; }
    public CatalogProduct? Product { get; init; }
}
