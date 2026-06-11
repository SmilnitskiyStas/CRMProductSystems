namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Order multiplier for a product scope during an event (v2-spec §4).
/// scope_type: category / segment / product; the most specific match wins.
/// </summary>
public sealed class DemandEventCoefficient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; init; }
    /// <summary>category / segment / product</summary>
    public string ScopeType { get; set; } = "category";
    public Guid? ScopeId { get; set; }
    public decimal Coefficient { get; set; } = 1.00m;
    /// <summary>manual / learned / ai_suggested</summary>
    public string Source { get; set; } = "manual";

    public DemandEvent? Event { get; init; }
}
