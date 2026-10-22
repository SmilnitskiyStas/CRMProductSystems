using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Production;

/// <summary>Derives <see cref="ProductionRequestStage"/> from a request, its lines and order links (no DB column).</summary>
public static class ProductionRequestStages
{
    /// <param name="links">Order links of the request's lines, with <c>ProductionOrder</c> loaded.</param>
    public static string Derive(
        string requestStatus,
        IReadOnlyCollection<ProductionRequestLine> lines,
        IReadOnlyCollection<ProductionOrderRequestLine> links)
    {
        if (requestStatus == ProductionRequestStatus.Fulfilled) return ProductionRequestStage.Shipped;
        if (requestStatus is not (ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial))
            return ProductionRequestStage.None;

        var active = links.Where(l => l.ProductionOrder is { Status: not ProductionOrderStatus.Cancelled }).ToList();
        var needed = lines.Where(l => l.AcceptedQty is > 0).ToList();
        if (needed.Count == 0) return ProductionRequestStage.None;

        // A line is unassigned when its linked qty does not cover the accepted qty (no recipe / cancelled order).
        foreach (var line in needed)
        {
            var linked = active.Where(l => l.RequestLineId == line.Id).Sum(l => l.Qty);
            if (linked < line.AcceptedQty!.Value) return ProductionRequestStage.AwaitingOrder;
        }

        var statuses = active.Select(l => l.ProductionOrder!.Status).ToList();
        if (statuses.All(s => s == ProductionOrderStatus.Planned)) return ProductionRequestStage.Planned;
        if (statuses.All(s => s == ProductionOrderStatus.Done)) return ProductionRequestStage.Ready;
        return ProductionRequestStage.InProduction;
    }

    /// <summary>True when the request has no live order, or every live order is Done (shipping from stock is allowed).</summary>
    public static bool AllOrdersDone(IEnumerable<ProductionOrderRequestLine> links) =>
        links.Where(l => l.ProductionOrder is { Status: not ProductionOrderStatus.Cancelled })
             .All(l => l.ProductionOrder!.Status == ProductionOrderStatus.Done);
}
