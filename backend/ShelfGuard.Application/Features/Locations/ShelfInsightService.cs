using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Locations;

/// <summary>
/// Product data for the planogram: stock on the shelf zone / in the store / in tenant warehouses
/// (location type "warehouse" or "central_warehouse"), nearest expiry (FEFO), sales windows and cover.
/// Exactly three bulk queries (stock, sales, ADU) however many items; everything runs through the
/// request's RLS-scoped DbContext, so scoped users only see stock they are allowed to see.
/// </summary>
public sealed class ShelfInsightService : IShelfInsightService
{
    private readonly ILocationRepository _locations;
    private readonly IShelfRepository _repo;

    /// <summary>Overridable clock for tests.</summary>
    public Func<DateOnly> Today { get; init; } = () => DateOnly.FromDateTime(DateTime.UtcNow);

    public ShelfInsightService(ILocationRepository locations, IShelfRepository repo)
    {
        _locations = locations;
        _repo = repo;
    }

    public async Task<(List<ShelfItemInsightDto>? Items, string? Error)> GetShelfInsightsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid shelfId, CancellationToken ct = default)
    {
        var zone = await _locations.GetZoneWithLocationAsync(zoneId, ct);
        if (zone is not { Location: not null } || zone.LocationId != locationId || zone.Location.TenantId != tenantId)
            return (null, "Zone not found.");

        var rows = await _repo.GetShelfItemsAsync(zoneId, shelfId, ct);
        return (await BuildAsync(locationId, zoneId, rows, ct), null);
    }

    public async Task<List<ShelfItemInsightDto>> BuildAsync(
        Guid locationId, Guid zoneId, IReadOnlyList<ShelfItem> planogram, CancellationToken ct = default)
    {
        var distinct = planogram
            .GroupBy(p => p.ItemId)
            .Select(g => (Row: g.First(), Facings: g.Sum(x => x.ExpectedFacings)))
            .ToList();
        if (distinct.Count == 0) return [];

        var ids = distinct.Select(d => d.Row.ItemId).ToList();
        var today = Today();

        var stock = await _repo.GetStockAggregatesAsync(ids, ct);
        var sales = (await _repo.GetSalesAggregatesAsync(locationId, ids, today, ct)).ToDictionary(s => s.ItemId);
        var adu = await _repo.GetAduAsync(locationId, ids, ct);
        var stockByItem = stock.ToLookup(s => s.ItemId);

        return distinct.Select(d =>
        {
            var id = d.Row.ItemId;
            var rows = stockByItem[id].ToList();
            var onShelf = rows.Where(r => r.LocationId == locationId && r.ZoneId == zoneId).Sum(r => r.Quantity);
            var store = rows.Where(r => r.LocationId == locationId).Sum(r => r.Quantity);
            var warehouse = rows.Where(r => IsWarehouse(r.LocationType)).Sum(r => r.Quantity);
            var expiries = rows.Where(r => r.LocationId == locationId).Select(r => r.NearestExpiry).ToList();
            DateOnly? expiry = expiries.Count > 0 ? expiries.Min() : null;

            sales.TryGetValue(id, out var sale);
            var s7 = sale?.Sold7d ?? 0m;
            var s30 = sale?.Sold30d ?? 0m;
            var prev = sale?.SoldPrev7d ?? 0m;
            // ADU: cached effective value, else a plain 30-day average from the sales we just loaded.
            var aduValue = adu.TryGetValue(id, out var a) ? a : Math.Round(s30 / 30m, 2);
            decimal? cover = aduValue > 0 ? Math.Round(store / aduValue, 1) : null;
            decimal? trend = prev > 0 ? Math.Round((s7 - prev) / prev * 100m, 1) : null;

            return new ShelfItemInsightDto(
                id, d.Row.Item?.Name ?? string.Empty, d.Row.Item?.Barcodes.FirstOrDefault(), d.Row.Item?.ImageUrl,
                d.Facings, onShelf, store, warehouse, expiry, s7, s30, aduValue, cover, trend);
        }).ToList();
    }

    private static bool IsWarehouse(string locationType) => locationType is "warehouse" or "central_warehouse";
}
