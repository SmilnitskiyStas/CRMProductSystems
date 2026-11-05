using ShelfGuard.Application.Features.Production.Dtos;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Automatic demand analysis for self-produced items (stage 3): detects stores that lack an
/// item made in-house (or whose item goes on promo / event uplift) and keeps ONE auto DRAFT
/// production request per (store, production location). Drafts are never submitted automatically.
/// </summary>
public interface IProductionDemandService
{
    /// <summary>
    /// Creates or refreshes auto drafts for the current tenant (all stores, or just
    /// <paramref name="storeLocationId"/>). Returns the created + changed drafts (unchanged ones
    /// are omitted). Tuple result: (drafts, error, statusCode).
    /// </summary>
    Task<(List<ProductionRequestDetailDto>? Drafts, string? Error, int? StatusCode)> GenerateAsync(
        Guid tenantId, Guid? userId, Guid? storeLocationId, CancellationToken ct = default);
}
