using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Orders;

public sealed class OrderCalcService : IOrderCalcService
{
    private readonly IOrderCalcRepository _repo;

    public OrderCalcService(IOrderCalcRepository repo) => _repo = repo;

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

        var lines = new List<OrderLineDto>();

        foreach (var buffer in buffers)
        {
            stock.TryGetValue(buffer.ProductId, out var onHand);
            inTransit.TryGetValue(buffer.ProductId, out var transit);
            var (moq, usq) = moqUsq.TryGetValue(buffer.ProductId, out var mu) ? mu : (1m, 1m);

            var safetyBuffer = buffer.Product?.SafetyBuffer ?? 0m;
            var calc = OrderFormula.Compute(
                buffer.BufferTotal, safetyBuffer, onHand, transit, moq, usq);

            lines.Add(new OrderLineDto(
                buffer.ProductId,
                buffer.Product?.Name ?? "",
                buffer.Product?.Barcode,
                buffer.BufferTotal,
                safetyBuffer,
                onHand,
                transit,
                calc.Raw,
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
        decimal moq, decimal usq)
    {
        var raw = buffer + safetyBuffer - stockOnHand - inTransit;

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
