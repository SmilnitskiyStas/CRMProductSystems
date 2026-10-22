namespace ShelfGuard.Domain.Interfaces;

/// <summary>One missing/low position with its deterministic recommendation, fed to the text summary.</summary>
public sealed record ShelfRestockLine(
    string Name, string Status, int ExpectedFacings, int EstimatedCount,
    decimal OnShelfQty, decimal StoreQty, decimal WarehouseQty, decimal Sold7d,
    string Action, int SuggestedQty, string Priority);

/// <summary>
/// Text-only advisor that turns the enriched scan result into a short Ukrainian manager summary.
/// Implemented in <c>Infrastructure/AI/ShelfRestockAdvisor</c>; optional - callers must treat any
/// failure as "no summary", never as a failed scan.
/// </summary>
public interface IShelfRestockAdvisor
{
    Task<string?> SummarizeAsync(IReadOnlyList<ShelfRestockLine> lines, CancellationToken ct = default);
}
