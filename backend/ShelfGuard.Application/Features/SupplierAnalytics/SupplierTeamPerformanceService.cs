using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;

namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <inheritdoc />
public sealed class SupplierTeamPerformanceService : ISupplierTeamPerformanceService
{
    /// <summary>Widest window the endpoint will report on — a larger request is clamped.</summary>
    public const int MaxWindowDays = 366;

    private readonly ISupplierTeamPerformanceRepository _repo;
    private readonly ISupplierCabinetService _cabinet;

    public SupplierTeamPerformanceService(
        ISupplierTeamPerformanceRepository repo, ISupplierCabinetService cabinet)
    {
        _repo    = repo;
        _cabinet = cabinet;
    }

    public async Task<SupplierTeamPerformanceDto> GetAsync(
        Guid supplierTenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber + 1 > MaxWindowDays)
            from = to.AddDays(-(MaxWindowDays - 1));

        var windowDays = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(windowDays - 1));

        var windowStart     = ToUtc(from);
        var windowEnd       = ToUtc(to.AddDays(1));           // exclusive
        var prevWindowStart = ToUtc(prevFrom);
        var prevWindowEnd   = windowStart;                    // prevTo.AddDays(1) == from

        var staff = await _cabinet.GetStaffAsync(supplierTenantId, ct);

        // One broad pull per data source from the earliest boundary we care about (prevFrom).
        // Marketplace volume is low (B2B) — the roll-up below is in memory.
        var orders  = await _repo.GetOrdersSinceAsync(supplierTenantId, prevWindowStart, ct);
        var reviews = await _repo.GetEmployeeReviewsSinceAsync(supplierTenantId, prevWindowStart, ct);
        var chat    = await _repo.GetChatMessagesSinceAsync(supplierTenantId, prevWindowStart, ct);

        var shippedOrderIds = orders
            .Where(o => o.ShippedByUserId is not null)
            .Select(o => o.OrderId)
            .Distinct()
            .ToList();
        var receipts = await _repo.GetFinalizedReceiptFlagsAsync(supplierTenantId, shippedOrderIds, ct);
        var finalizedOrders   = receipts.Select(r => r.OrderId).ToHashSet();
        var discrepancyByOrder = receipts.ToDictionary(r => r.OrderId, r => r.HasDiscrepancy);

        var employees = staff
            .Select(u =>
            {
                var current = ComputeWindow(
                    u.Id, supplierTenantId, windowStart, windowEnd,
                    orders, finalizedOrders, discrepancyByOrder, chat, reviews);
                var previous = ComputeWindow(
                    u.Id, supplierTenantId, prevWindowStart, prevWindowEnd,
                    orders, finalizedOrders, discrepancyByOrder, chat, reviews);

                return new SupplierEmployeePerformanceDto(
                    u.Id,
                    u.FullName,
                    current.OrdersConfirmed,
                    current.OrdersShipped,
                    Round2(current.AvgHoursToConfirm),
                    Round2(current.AvgHoursToShip),
                    Round4(current.OnTimeDeliveryRate),
                    Round4(current.DiscrepancyFreeRate),
                    current.ChatMessagesSent,
                    current.ChatSessionsHandled,
                    Round2(current.MedianFirstResponseHours),
                    Round2(current.AvgBuyerRating),
                    current.BuyerReviewCount,
                    PeriodMetricDto.Of(current.OrdersShipped, previous.OrdersShipped),
                    PeriodMetricDto.Of(
                        (decimal)(current.OnTimeDeliveryRate ?? 0d),
                        (decimal)(previous.OnTimeDeliveryRate ?? 0d)),
                    PeriodMetricDto.Of(
                        (decimal)(current.AvgBuyerRating ?? 0d),
                        (decimal)(previous.AvgBuyerRating ?? 0d)));
            })
            .OrderBy(e => e.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SupplierTeamPerformanceDto(from, to, employees);
    }

    public async Task<IReadOnlyList<SupplierEmployeeReviewDetailDto>> GetEmployeeReviewsAsync(
        Guid supplierTenantId, Guid supplierUserId, CancellationToken ct = default)
    {
        var rows = await _repo.GetEmployeeReviewDetailsAsync(supplierTenantId, supplierUserId, ct);
        return rows
            .Select(r => new SupplierEmployeeReviewDetailDto(
                r.Id, r.SupplierUserId, r.SupplierUserName, r.Rating, r.Comment,
                r.Source, r.OrderId, r.ChatSessionId, r.RatedByName, r.CreatedAt))
            .ToList();
    }

    // ── Per-employee, per-window roll-up ──────────────────────────────────────

    private sealed record EmpWindow(
        int OrdersConfirmed,
        int OrdersShipped,
        double? AvgHoursToConfirm,
        double? AvgHoursToShip,
        double? OnTimeDeliveryRate,
        double? DiscrepancyFreeRate,
        int ChatMessagesSent,
        int ChatSessionsHandled,
        double? MedianFirstResponseHours,
        double? AvgBuyerRating,
        int BuyerReviewCount);

    private static EmpWindow ComputeWindow(
        Guid userId, Guid supplierTenantId,
        DateTimeOffset windowStart, DateTimeOffset windowEnd,
        IReadOnlyList<TeamPerfOrderRow> orders,
        IReadOnlySet<Guid> finalizedOrders,
        IReadOnlyDictionary<Guid, bool> discrepancyByOrder,
        IReadOnlyList<TeamPerfChatMessageRow> chat,
        IReadOnlyList<TeamPerfReviewRow> reviews)
    {
        bool InWindow(DateTimeOffset? ts) => ts is { } t && t >= windowStart && t < windowEnd;

        // ── Orders confirmed (windowed by CreatedAt — keeps historical confirms countable even
        //    though their ConfirmedAt is null) ──
        var confirmed = orders
            .Where(o => o.ConfirmedByUserId == userId && InWindow(o.CreatedAt))
            .ToList();
        var confirmTimings = confirmed
            .Where(o => o.ConfirmedAt is not null)
            .Select(o => (o.ConfirmedAt!.Value - o.CreatedAt).TotalHours)
            .ToList();

        // ── Orders shipped (windowed by ShippedAt) ──
        var shipped = orders
            .Where(o => o.ShippedByUserId == userId && InWindow(o.ShippedAt))
            .ToList();
        var shipTimings = shipped
            .Where(o => o.ConfirmedAt is not null && o.ShippedAt is not null)
            .Select(o => (o.ShippedAt!.Value - o.ConfirmedAt!.Value).TotalHours)
            .ToList();

        // ── On-time delivery ──
        var deliveredWithEta = shipped
            .Where(o => o.DeliveredAt is not null && o.ExpectedDeliveryDate is not null)
            .ToList();
        var onTimeCount = deliveredWithEta.Count(o =>
            DateOnly.FromDateTime(o.DeliveredAt!.Value.UtcDateTime) <= o.ExpectedDeliveryDate!.Value);
        double? onTimeRate = deliveredWithEta.Count > 0
            ? onTimeCount / (double)deliveredWithEta.Count
            : null;

        // ── Discrepancy-free receiving ──
        var received = shipped.Where(o => finalizedOrders.Contains(o.OrderId)).ToList();
        var cleanCount = received.Count(o => !discrepancyByOrder.GetValueOrDefault(o.OrderId));
        double? discrepancyFreeRate = received.Count > 0
            ? cleanCount / (double)received.Count
            : null;

        // ── Chat ──
        var myMessages = chat
            .Where(m => m.SenderUserId == userId && m.SenderTenantId == supplierTenantId)
            .ToList();
        var myMessagesInWindow = myMessages
            .Where(m => m.CreatedAt >= windowStart && m.CreatedAt < windowEnd)
            .ToList();
        var handledSessions = myMessagesInWindow.Select(m => m.SessionId).Distinct().ToList();

        var firstResponseGaps = new List<double>();
        foreach (var sessionId in handledSessions)
        {
            var firstReply = myMessagesInWindow
                .Where(m => m.SessionId == sessionId)
                .Min(m => m.CreatedAt);
            var precedingClient = chat
                .Where(m => m.SessionId == sessionId
                         && m.SenderTenantId != supplierTenantId
                         && m.CreatedAt <= firstReply)
                .ToList();
            if (precedingClient.Count > 0)
                firstResponseGaps.Add((firstReply - precedingClient.Max(m => m.CreatedAt)).TotalHours);
        }

        // ── Buyer ratings (windowed by CreatedAt) ──
        var ratings = reviews
            .Where(r => r.SupplierUserId == userId
                     && r.CreatedAt >= windowStart && r.CreatedAt < windowEnd)
            .Select(r => (double)r.Rating)
            .ToList();

        return new EmpWindow(
            OrdersConfirmed:          confirmed.Count,
            OrdersShipped:            shipped.Count,
            AvgHoursToConfirm:        Mean(confirmTimings),
            AvgHoursToShip:           Mean(shipTimings),
            OnTimeDeliveryRate:       onTimeRate,
            DiscrepancyFreeRate:      discrepancyFreeRate,
            ChatMessagesSent:         myMessagesInWindow.Count,
            ChatSessionsHandled:      handledSessions.Count,
            MedianFirstResponseHours: Median(firstResponseGaps),
            AvgBuyerRating:           ratings.Count > 0 ? ratings.Average() : null,
            BuyerReviewCount:         ratings.Count);
    }

    // ── Small numeric helpers ────────────────────────────────────────────────

    private static DateTimeOffset ToUtc(DateOnly d) =>
        new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static double? Mean(IReadOnlyCollection<double> values) =>
        values.Count > 0 ? values.Average() : null;

    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2d;
    }

    private static double? Round2(double? v) => v is null ? null : Math.Round(v.Value, 2);
    private static double? Round4(double? v) => v is null ? null : Math.Round(v.Value, 4);
}
