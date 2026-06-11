using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Orders;

public sealed class OrderCalcService : IOrderCalcService
{
    private readonly IOrderCalcRepository _repo;
    private readonly IEventRepository _events;
    private readonly IWeatherRepository _weather;
    private readonly ICannibalizationRepository _promo;

    public OrderCalcService(
        IOrderCalcRepository repo, IEventRepository events,
        IWeatherRepository weather, ICannibalizationRepository promo)
    {
        _repo = repo;
        _events = events;
        _weather = weather;
        _promo = promo;
    }

    public async Task<(OrderCalcResult? Result, string? Error)> CalculateAsync(
        Guid storeId, CancellationToken ct = default)
    {
        if (!await _repo.StoreExistsAsync(storeId, ct))
            return (null, "Store not found.");

        var buffers = await _repo.GetBuffersAsync(storeId, ct);
        if (buffers.Count == 0)
            return (new OrderCalcResult(storeId, DateTime.UtcNow, 0, 0, []), null);

        var productIds = buffers.Select(b => b.ProductId).ToList();
        var stock = await _repo.GetStockOnHandAsync(storeId, productIds, ct);
        var inTransit = await _repo.GetInTransitAsync(storeId, productIds, ct);
        var moqUsq = await _repo.GetMoqUsqAsync(productIds, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeEvents = await _events.GetCandidatesForDateAsync(storeId, today, ct);
        var todayWeather = await _weather.GetForDateAsync(storeId, today, ct);
        var weatherRules = await _weather.GetCoefficientsAsync(ct);
        var promoCoefs = await _promo.GetActivePromoCoefficientsAsync(storeId, DateTime.UtcNow, ct);

        var lines = new List<OrderLineDto>();

        foreach (var buffer in buffers)
        {
            stock.TryGetValue(buffer.ProductId, out var onHand);
            inTransit.TryGetValue(buffer.ProductId, out var transit);
            var (moq, usq) = moqUsq.TryGetValue(buffer.ProductId, out var mu) ? mu : (1m, 1m);

            var eventCoef = EventCoefficientResolver.Resolve(
                activeEvents, buffer.ProductId, buffer.Product?.SegmentId, buffer.Product?.CategoryId);
            var weatherCoef = Features.Weather.WeatherCoefficientResolver.Resolve(
                todayWeather, weatherRules, buffer.Product?.SegmentId, buffer.Product?.CategoryId);
            var promoCoef = promoCoefs.TryGetValue(buffer.ProductId, out var pk) ? pk : 1m;

            var safetyBuffer = buffer.Product?.SafetyBuffer ?? 0m;
            var calc = OrderFormula.Compute(
                buffer.BufferTotal, safetyBuffer, onHand, transit, moq, usq,
                demandMultiplier: eventCoef * weatherCoef * promoCoef);

            lines.Add(new OrderLineDto(
                buffer.ProductId,
                buffer.Product?.Name ?? "",
                buffer.Product?.Barcode,
                buffer.BufferTotal,
                buffer.BufferGreen,
                buffer.BufferYellow,
                buffer.BufferRed,
                safetyBuffer,
                onHand,
                transit,
                calc.Raw,
                eventCoef,
                weatherCoef,
                promoCoef,
                calc.ToOrder,
                moq,
                usq,
                calc.Rounding));
        }

        var ordered = lines
            .OrderByDescending(l => l.QuantityToOrder > 0)
            .ThenBy(l => l.ProductName)
            .ToList();

        return (new OrderCalcResult(
            storeId,
            DateTime.UtcNow,
            ordered.Count,
            ordered.Count(l => l.QuantityToOrder > 0),
            ordered), null);
    }
}

/// <summary>
/// Pure order formula (v2-spec §3):
///   Raw = Buffer + SafetyBuffer − StockOnHand − InTransit
///   Raw ≤ 0            → order 0 (covered)
///   0 < Raw ≤ MOQ      → order MOQ (supplier minimum)
///   Raw > MOQ          → round to nearest USQ multiple (математично), never below MOQ
/// (ОЗ one-off and РТО reserved-for-customer terms arrive with MTO support — currently 0.)
/// </summary>
internal static class OrderFormula
{
    internal sealed record OrderQty(decimal Raw, decimal ToOrder, string Rounding);

    internal static OrderQty Compute(
        decimal buffer, decimal safetyBuffer, decimal stockOnHand, decimal inTransit,
        decimal moq, decimal usq, decimal demandMultiplier = 1m)
    {
        var raw = buffer + safetyBuffer - stockOnHand - inTransit;

        // Event/weather/promo multipliers scale the demand (v2-spec §3) before rounding.
        if (raw > 0 && demandMultiplier != 1m)
            raw = Math.Round(raw * demandMultiplier, 2);

        if (raw <= 0)
            return new OrderQty(raw, 0m, "none");

        if (moq < 1) moq = 1;
        if (usq <= 0) usq = 1;

        if (raw <= moq)
            return new OrderQty(raw, moq, "moq_floor");

        var rounded = Math.Round(raw / usq, MidpointRounding.AwayFromZero) * usq;
        if (rounded < moq) rounded = moq;

        return new OrderQty(raw, rounded, "usq_rounded");
    }
}

/// <summary>
/// Resolves the event multiplier for a product (v2-spec §4).
/// Within one event the most specific matching coefficient wins
/// (product > segment > category; null ScopeId = whole scope class).
/// Multipliers from different events multiply (independent factors, §3).
/// </summary>
internal static class EventCoefficientResolver
{
    internal static decimal Resolve(
        IReadOnlyList<DemandEvent> activeEvents,
        Guid productId, Guid? segmentId, Guid? categoryId)
    {
        var total = 1m;

        foreach (var ev in activeEvents)
        {
            var best =
                ev.Coefficients.FirstOrDefault(c => c.ScopeType == "product" && c.ScopeId == productId)
                ?? (segmentId is not null
                    ? ev.Coefficients.FirstOrDefault(c => c.ScopeType == "segment" && c.ScopeId == segmentId)
                    : null)
                ?? (categoryId is not null
                    ? ev.Coefficients.FirstOrDefault(c => c.ScopeType == "category" && c.ScopeId == categoryId)
                    : null);

            if (best is not null)
                total *= best.Coefficient;
        }

        return Math.Round(total, 2);
    }
}
