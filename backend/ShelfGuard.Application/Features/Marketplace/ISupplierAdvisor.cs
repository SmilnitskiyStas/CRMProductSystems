using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// AI-backed supplier recommendation advisor (v4-spec Phase 3).
/// Implemented by <c>SupplierAdvisor</c> in Infrastructure/AI — provider
/// details and prompt templates never leak past this interface.
/// </summary>
public interface ISupplierAdvisor
{
    /// <summary>True when an API key is available (integration config or environment).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Given a procurement request and a list of candidate suppliers, returns
    /// a ranked recommendation list with Ukrainian-language reasoning.
    /// </summary>
    Task<SupplierRecommendationResult> RecommendAsync(
        SupplierRecommendationRequest request,
        IEnumerable<SupplierCandidateDto> candidates,
        CancellationToken ct = default);
}

// ── Request / Result records ──────────────────────────────────────────────────

public sealed record SupplierRecommendationRequest(
    string ItemName,
    string? Region,
    int? RequiredQty,
    string? Notes);

/// <summary>Compact supplier snapshot passed as context to the AI.</summary>
public sealed record SupplierCandidateDto(
    Guid SupplierId,
    string SupplierName,
    string? Region,
    decimal? Rating,
    decimal? AvgDeliveryDays,
    decimal? OrderAccuracy,
    string? MatchedItemName,
    decimal? MatchedItemPrice,
    string? MatchedItemUnit,
    int? MatchedItemMinQty);

public sealed record SupplierRecommendationResult(
    IReadOnlyList<SupplierRecommendationItem> Recommendations,
    string Prompt,
    string Model,
    int TokensUsed);

public sealed record SupplierRecommendationItem(
    Guid SupplierId,
    int Rank,
    decimal Score,
    string Reasoning,
    string? MatchedItemName,
    decimal? MatchedItemPrice);
