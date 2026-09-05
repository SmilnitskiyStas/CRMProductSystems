using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;
using ShelfGuard.Application.Features.Users.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-695 (Phase 8): the in-memory per-employee roll-up <see cref="SupplierTeamPerformanceService"/>
/// performs over the orders / chat messages / buyer ratings the repository hands it — order
/// throughput windowed per actor, confirm/ship timing means, on-time-delivery and
/// discrepancy-free rates, chat message/session counts + median first-response, buyer-rating
/// average, and the period-over-period deltas. RLS-scoped fetching is the repository's job.
/// </summary>
public sealed class SupplierTeamPerformanceServiceTests
{
    private readonly ISupplierTeamPerformanceRepository _repo =
        Substitute.For<ISupplierTeamPerformanceRepository>();
    private readonly ISupplierCabinetService _cabinet = Substitute.For<ISupplierCabinetService>();
    private readonly SupplierTeamPerformanceService _sut;

    private readonly Guid _tid = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To   = new(2026, 8, 31);

    public SupplierTeamPerformanceServiceTests()
    {
        _sut = new SupplierTeamPerformanceService(_repo, _cabinet);

        _cabinet.GetStaffAsync(_tid, Arg.Any<CancellationToken>()).Returns(new[]
        {
            Staff(_alice, "Аліса"),
            Staff(_bob, "Богдан"),
        });

        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TeamPerfOrderRow>());
        _repo.GetFinalizedReceiptFlagsAsync(_tid, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TeamPerfReceiptRow>());
        _repo.GetChatMessagesSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TeamPerfChatMessageRow>());
        _repo.GetEmployeeReviewsSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TeamPerfReviewRow>());
    }

    private static UserDto Staff(Guid id, string name) =>
        new(id, $"{id:N}@s.com", name, null, "supplier_admin", null, true, false, DateTime.UtcNow, null);

    private static DateTimeOffset Dt(int day, int hour = 10) =>
        new(2026, 8, day, hour, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Jul(int day) => new(2026, 7, day, 10, 0, 0, TimeSpan.Zero);

    private TeamPerfOrderRow Order(
        Guid? confirmedBy = null, Guid? shippedBy = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? confirmedAt = null,
        DateTimeOffset? shippedAt = null, DateTimeOffset? deliveredAt = null,
        DateOnly? expectedDelivery = null, Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), confirmedBy, shippedBy,
            createdAt ?? Dt(2), confirmedAt, shippedAt, deliveredAt, expectedDelivery);

    private SupplierEmployeePerformanceDto Row(SupplierTeamPerformanceDto dto, Guid userId) =>
        Assert.Single(dto.Employees, e => e.UserId == userId);

    // ── order counts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OrdersConfirmedAndShipped_AreCountedPerActorAndWindowedByCreatedAtAndShippedAt()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(confirmedBy: _alice, createdAt: Dt(3), shippedBy: _alice, shippedAt: Dt(5)),
            // Confirmed by Alice but CREATED in the previous window → not in her current count.
            Order(confirmedBy: _alice, createdAt: Jul(15), shippedBy: _bob, shippedAt: Dt(10)),
            Order(confirmedBy: _bob, createdAt: Dt(20)),
        });

        var dto = await _sut.GetAsync(_tid, From, To);

        var alice = Row(dto, _alice);
        Assert.Equal(1, alice.OrdersConfirmed);
        Assert.Equal(1, alice.OrdersShipped);

        var bob = Row(dto, _bob);
        Assert.Equal(1, bob.OrdersConfirmed);
        Assert.Equal(1, bob.OrdersShipped);
    }

    // ── timing means ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TimingMeans_AvgHoursToConfirmAndToShip()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(confirmedBy: _alice, createdAt: Dt(3, 10), confirmedAt: Dt(3, 16),
                  shippedBy: _alice, shippedAt: Dt(5, 16)),
            Order(confirmedBy: _alice, createdAt: Dt(6, 10), confirmedAt: Dt(6, 12),
                  shippedBy: _alice, shippedAt: Dt(6, 22)),
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(4d, alice.AvgHoursToConfirm);   // (6 + 2) / 2
        Assert.Equal(29d, alice.AvgHoursToShip);      // (48 + 10) / 2
    }

    [Fact]
    public async Task TimingMeans_NullWhenNoDataOrConfirmedAtMissing()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            // Legacy order — confirmed & shipped in-window but no ConfirmedAt snapshot.
            Order(confirmedBy: _alice, createdAt: Dt(3), confirmedAt: null,
                  shippedBy: _alice, shippedAt: Dt(5)),
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(1, alice.OrdersConfirmed);
        Assert.Null(alice.AvgHoursToConfirm);
        Assert.Null(alice.AvgHoursToShip);
    }

    // ── on-time delivery ─────────────────────────────────────────────────────

    [Fact]
    public async Task OnTimeDeliveryRate_FractionOfShippedAndDeliveredWithinTheEta()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(shippedBy: _alice, shippedAt: Dt(4), deliveredAt: Dt(8), expectedDelivery: new DateOnly(2026, 8, 9)),
            Order(shippedBy: _alice, shippedAt: Dt(6), deliveredAt: Dt(20), expectedDelivery: new DateOnly(2026, 8, 15)),
            // No ETA → excluded from the denominator entirely.
            Order(shippedBy: _alice, shippedAt: Dt(7), deliveredAt: Dt(9), expectedDelivery: null),
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(0.5d, alice.OnTimeDeliveryRate);
    }

    [Fact]
    public async Task OnTimeDeliveryRate_NullWhenNoDeliveredOrdersWithEta()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(shippedBy: _alice, shippedAt: Dt(4)),
        });

        Assert.Null(Row(await _sut.GetAsync(_tid, From, To), _alice).OnTimeDeliveryRate);
    }

    // ── discrepancy-free receiving ───────────────────────────────────────────

    [Fact]
    public async Task DiscrepancyFreeRate_OverShippedOrdersThatHaveAFinalizedReceipt()
    {
        var clean = Guid.NewGuid();
        var flagged = Guid.NewGuid();
        var noReceipt = Guid.NewGuid();

        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(shippedBy: _alice, shippedAt: Dt(4), orderId: clean),
            Order(shippedBy: _alice, shippedAt: Dt(5), orderId: flagged),
            Order(shippedBy: _alice, shippedAt: Dt(6), orderId: noReceipt),
        });
        _repo.GetFinalizedReceiptFlagsAsync(_tid, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TeamPerfReceiptRow(clean, HasDiscrepancy: false),
                new TeamPerfReceiptRow(flagged, HasDiscrepancy: true),
            });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(0.5d, alice.DiscrepancyFreeRate);   // 1 clean of 2 with a finalized receipt
    }

    // ── chat ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Chat_MessageAndSessionCounts_AndMedianFirstResponse()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();

        _repo.GetChatMessagesSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            // S1 — client asks 09:00, Alice first replies 11:00 (gap 2h), then again 08:30 next day.
            new TeamPerfChatMessageRow(s1, Guid.NewGuid(), _clientTenantId, Dt(4, 9)),
            new TeamPerfChatMessageRow(s1, _alice, _tid, Dt(4, 11)),
            new TeamPerfChatMessageRow(s1, _alice, _tid, Dt(5, 8)),
            // S2 — client asks 10:00, Alice replies 14:00 (gap 4h).
            new TeamPerfChatMessageRow(s2, Guid.NewGuid(), _clientTenantId, Dt(10, 10)),
            new TeamPerfChatMessageRow(s2, _alice, _tid, Dt(10, 14)),
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(3, alice.ChatMessagesSent);
        Assert.Equal(2, alice.ChatSessionsHandled);
        Assert.Equal(3d, alice.MedianFirstResponseHours);   // median(2, 4)
    }

    // ── buyer ratings + delta ────────────────────────────────────────────────

    [Fact]
    public async Task BuyerRating_AvgAndCount_Windowed_WithPeriodOverPeriodDelta()
    {
        _repo.GetEmployeeReviewsSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new TeamPerfReviewRow(_alice, 4, Dt(5)),
            new TeamPerfReviewRow(_alice, 2, Dt(20)),
            new TeamPerfReviewRow(_alice, 5, Jul(10)),     // previous window
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(3d, alice.AvgBuyerRating);
        Assert.Equal(2, alice.BuyerReviewCount);
        Assert.Equal(3m, alice.AvgBuyerRatingDelta.Current);
        Assert.Equal(5m, alice.AvgBuyerRatingDelta.Previous);
        Assert.Equal(-40m, alice.AvgBuyerRatingDelta.PercentChange);
    }

    [Fact]
    public async Task OrdersShippedDelta_ComparesToTheEqualLengthPrecedingWindow()
    {
        _repo.GetOrdersSinceAsync(_tid, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Order(shippedBy: _alice, shippedAt: Dt(4)),
            Order(shippedBy: _alice, shippedAt: Dt(9)),
            Order(shippedBy: _alice, shippedAt: Jul(20)),   // previous window
        });

        var alice = Row(await _sut.GetAsync(_tid, From, To), _alice);

        Assert.Equal(2, alice.OrdersShipped);
        Assert.Equal(2m, alice.OrdersShippedDelta.Current);
        Assert.Equal(1m, alice.OrdersShippedDelta.Previous);
        Assert.Equal(100m, alice.OrdersShippedDelta.PercentChange);
    }

    // ── shape ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StaffWithNoActivity_StillAppears_WithZeroesAndNulls()
    {
        var dto = await _sut.GetAsync(_tid, From, To);

        Assert.Equal(2, dto.Employees.Count);
        var bob = Row(dto, _bob);
        Assert.Equal(0, bob.OrdersConfirmed);
        Assert.Equal(0, bob.OrdersShipped);
        Assert.Null(bob.AvgHoursToConfirm);
        Assert.Null(bob.OnTimeDeliveryRate);
        Assert.Null(bob.AvgBuyerRating);
        Assert.Equal(0, bob.BuyerReviewCount);
        // Ordered by name.
        Assert.Equal(_alice, dto.Employees[0].UserId);
        Assert.Equal(_bob, dto.Employees[1].UserId);
    }

    [Fact]
    public async Task Window_WiderThan366Days_IsClampedByMovingFromForward()
    {
        var wideFrom = new DateOnly(2024, 1, 1);
        var wideTo = new DateOnly(2026, 1, 1);

        var dto = await _sut.GetAsync(_tid, wideFrom, wideTo);

        Assert.Equal(wideTo, dto.To);
        Assert.Equal(wideTo.AddDays(-365), dto.From);
    }
}
