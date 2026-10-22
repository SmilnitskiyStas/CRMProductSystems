namespace ShelfGuard.Application.Features.Analytics.PromoNoSales;

public interface IPromoNoSalesRepository
{
    /// <summary>Running promos (discounts, published campaigns, published rules) × stores that hold stock.</summary>
    Task<IReadOnlyList<PromoNoSalesCandidate>> GetCandidatesAsync(
        Guid tenantId, Guid? storeId, DateOnly today, CancellationToken ct = default);
}

public interface IPromoNoSalesService
{
    Task<PromoNoSalesDto> GetAsync(Guid tenantId, Guid? storeId, CancellationToken ct = default);
}
