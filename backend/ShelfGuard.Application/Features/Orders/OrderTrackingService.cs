using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Orders;

public sealed class OrderTrackingService : IOrderTrackingService
{
    private static readonly HashSet<string> OpenStatuses = ["new", "confirmed", "in_transit"];
    private static readonly HashSet<string> KnownStatuses = ["new", "confirmed", "in_transit", "delivered", "cancelled"];

    private readonly IOrderTrackingRepository _repo;

    public OrderTrackingService(IOrderTrackingRepository repo) => _repo = repo;

    public async Task<OrderTrackingPageDto> GetPagedAsync(
        Guid tenantId, string? source, IReadOnlyCollection<string>? statuses, Guid? storeId, string? search,
        int page, int pageSize, bool overdueOnly = false, CancellationToken ct = default)
    {
        var p = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);
        var src = source is "receipt" or "marketplace" ? source : null;
        var st = statuses?.Where(KnownStatuses.Contains).Distinct().ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var (rows, total) = await _repo.GetPagedAsync(tenantId, src, st, storeId, search, p, size, overdueOnly, today, ct);

        var items = rows.Select(r =>
        {
            var overdue = r.ExpectedAt is { } e && e < today && OpenStatuses.Contains(r.Status);
            return new TrackedOrderDto(
                r.Source, r.Id, r.Number, r.Status, r.Counterparty, r.StoreId, r.StoreName, r.CreatedAt,
                r.ExpectedAt, overdue, overdue ? today.DayNumber - r.ExpectedAt!.Value.DayNumber : null,
                r.TotalQuantity, r.TotalAmount, r.Note, BuildTimeline(r),
                r.Lines.Select(l => new TrackedOrderLineDto(l.Name, l.Quantity, l.QuantityReceived, l.Unit, l.Amount)).ToList(),
                r.PostponeCount, r.OriginalExpectedAt, r.PostponedAt,
                r.Source == "receipt" && OpenStatuses.Contains(r.Status));
        }).ToList();

        return new OrderTrackingPageDto(items, total, p, size, (int)Math.Ceiling(total / (double)size));
    }

    public async Task<OrderTrackingSummaryDto> GetSummaryAsync(Guid tenantId, Guid? storeId, CancellationToken ct = default)
    {
        var s = await _repo.GetSummaryAsync(tenantId, storeId, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        return new OrderTrackingSummaryDto(
            s.New, s.Confirmed, s.InTransit, s.Delivered, s.Cancelled, s.Overdue, s.New + s.Confirmed + s.InTransit,
            s.OpenQuantity, s.InTransitQuantity, s.OverdueQuantity, s.Postponed);
    }

    /// <summary>Steps are always the same set per source so the UI can draw one stepper; a step with no
    /// timestamp has not happened (or is not tracked for that source).</summary>
    private static List<OrderTimelineStepDto> BuildTimeline(TrackedOrderRow r)
    {
        var steps = new List<OrderTimelineStepDto> { new("created", r.CreatedAt, r.CreatedBy) };
        if (r.Source == "marketplace")
        {
            steps.Add(new("confirmed", r.ConfirmedAt, r.ConfirmedBy));
            steps.Add(new("shipped", r.ShippedAt, r.ShippedBy));
        }
        steps.Add(new(r.Status == "cancelled" ? "cancelled" : "delivered", r.ClosedAt, r.ClosedBy));
        return steps;
    }
}
