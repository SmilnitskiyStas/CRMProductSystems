using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <inheritdoc />
public sealed class SupplierAnalyticsService : ISupplierAnalyticsService
{
    /// <summary>Widest window the endpoint will report on — a larger request is clamped.</summary>
    public const int MaxWindowDays = 366;

    private const int TopN = 10;
    private const string UnknownBuyerName = "Замовник";

    private readonly ISupplierAnalyticsRepository _repo;
    private readonly ISupplierChatRepository _tenantNames;

    public SupplierAnalyticsService(
        ISupplierAnalyticsRepository repo, ISupplierChatRepository tenantNames)
    {
        _repo = repo;
        _tenantNames = tenantNames;
    }

    public async Task<SupplierAnalyticsDto> GetAsync(
        Guid supplierTenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber + 1 > MaxWindowDays)
            from = to.AddDays(-(MaxWindowDays - 1));

        var windowDays = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(windowDays - 1));

        var currentLines = await _repo.GetOrderLinesAsync(supplierTenantId, from, to, ct);
        var previousLines = await _repo.GetOrderLinesAsync(supplierTenantId, prevFrom, prevTo, ct);
        var catalog = await _repo.GetAvailableCatalogAsync(supplierTenantId, ct);

        var totalRevenue = currentLines.Sum(l => l.LineTotal);
        var orderCount = currentLines.Select(l => l.OrderId).Distinct().Count();
        var itemsSold = currentLines.Sum(l => l.Qty);

        var prevRevenue = previousLines.Sum(l => l.LineTotal);
        var prevOrderCount = previousLines.Select(l => l.OrderId).Distinct().Count();
        var prevItemsSold = previousLines.Sum(l => l.Qty);

        // Group by (SupplierItemId, name-snapshot): a null id (deleted catalog entry) still groups
        // by its frozen line name, and a renamed item groups under each name it was ordered as.
        var byItem = currentLines
            .GroupBy(l => new { l.SupplierItemId, l.ItemName })
            .Select(g => new SupplierAnalyticsItemDto(
                g.Key.SupplierItemId,
                g.Key.ItemName,
                g.Sum(x => x.Qty),
                g.Sum(x => x.LineTotal),
                g.Select(x => x.OrderId).Distinct().Count()))
            .ToList();

        var topItems = byItem
            .OrderByDescending(x => x.QtySold)
            .ThenByDescending(x => x.Revenue)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .Take(TopN)
            .ToList();

        // Slow movers: LEFT JOIN the demand aggregates onto the available catalog so a
        // never-ordered item surfaces with zero demand.
        var demandByItemId = byItem
            .Where(x => x.SupplierItemId is not null)
            .ToDictionary(x => x.SupplierItemId!.Value);

        var slowItems = catalog
            .Select(c => demandByItemId.TryGetValue(c.SupplierItemId, out var agg)
                ? agg with { ItemName = c.ItemName }
                : new SupplierAnalyticsItemDto(c.SupplierItemId, c.ItemName, 0m, 0m, 0))
            .OrderBy(x => x.QtySold)
            .ThenBy(x => x.Revenue)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .Take(TopN)
            .ToList();

        var byBuyer = new List<SupplierAnalyticsBuyerDto>();
        foreach (var g in currentLines
                     .GroupBy(l => l.ClientTenantId)
                     .OrderByDescending(g => g.Sum(x => x.LineTotal)))
        {
            var name = await _tenantNames.GetTenantDisplayNameAsync(g.Key, ct) ?? UnknownBuyerName;
            byBuyer.Add(new SupplierAnalyticsBuyerDto(
                g.Key,
                name,
                g.Select(x => x.OrderId).Distinct().Count(),
                g.Sum(x => x.LineTotal)));
        }

        var revenueTrend = currentLines
            .GroupBy(l => DateOnly.FromDateTime(l.OrderCreatedAt.UtcDateTime))
            .Select(g => new SupplierAnalyticsTrendPointDto(
                g.Key,
                g.Sum(x => x.LineTotal),
                g.Select(x => x.OrderId).Distinct().Count()))
            .OrderBy(p => p.Date)
            .ToList();

        return new SupplierAnalyticsDto(
            from,
            to,
            totalRevenue,
            orderCount,
            itemsSold,
            PeriodMetricDto.Of(totalRevenue, prevRevenue),
            PeriodMetricDto.Of(orderCount, prevOrderCount),
            PeriodMetricDto.Of(itemsSold, prevItemsSold),
            topItems,
            slowItems,
            byBuyer,
            revenueTrend);
    }
}
