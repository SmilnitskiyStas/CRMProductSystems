namespace ShelfGuard.Application.Features.Analytics.PromoNoSales;

public sealed class PromoNoSalesService : IPromoNoSalesService
{
    private readonly IPromoNoSalesRepository _repo;

    public PromoNoSalesService(IPromoNoSalesRepository repo) => _repo = repo;

    public async Task<PromoNoSalesDto> GetAsync(Guid tenantId, Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidates = await _repo.GetCandidatesAsync(tenantId, storeId, today, ct);

        var items = new List<PromoNoSalesItemDto>();
        foreach (var c in candidates)
        {
            var v = PromoNoSalesDetector.Evaluate(c.Promo, today, c.Adu, c.StoreStock, c.SalesByDate);
            if (v is null) continue;
            items.Add(new PromoNoSalesItemDto(
                c.StoreId, c.StoreName, c.ProductId, c.ProductName, c.Barcode,
                c.Promo.Source, c.Promo.SourceId, c.Promo.Name, c.DiscountPercent,
                c.Promo.StartDate, c.Promo.EndDate, v.From, v.To, v.Days,
                v.UnitsSold, v.ExpectedUnits, c.StoreStock, c.OnShelfStock, c.WarehouseStock,
                c.ZoneId, c.ZoneName, v.Reason));
        }

        // Most suspicious first: no sales before low sales, then the largest stock.
        items = items
            .OrderBy(i => i.Reason == PromoNoSalesReasons.NoSales ? 0 : 1)
            .ThenByDescending(i => i.StoreStock)
            .ToList();

        return new PromoNoSalesDto(today, PromoNoSalesThresholds.MinPromoDays, PromoNoSalesThresholds.MaxSalesRatio, items);
    }
}
