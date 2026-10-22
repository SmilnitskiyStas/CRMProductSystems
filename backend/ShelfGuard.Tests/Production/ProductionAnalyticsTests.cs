using ShelfGuard.Application.Features.Production;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Production;

/// <summary>TASK-767: GET /api/production/analytics read model.</summary>
public sealed class ProductionAnalyticsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private sealed class FixedClock : TimeProvider
    {
        // 2026-10-21 12:00 UTC => Kyiv (UTC+3) local date 2026-10-21.
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 10, 21);
    private readonly FakeInsightsRepo _repo = new();
    private readonly Location _prod = new() { TenantId = Tenant, Name = "Цех 1", Type = "production", TimeZone = "Europe/Kyiv" };
    private readonly Location _prod2 = new() { TenantId = Tenant, Name = "Цех 2", Type = "production", TimeZone = "Europe/Kyiv" };
    private readonly Location _shopA = new() { TenantId = Tenant, Name = "Магазин А", Type = "shop" };
    private readonly Location _shopB = new() { TenantId = Tenant, Name = "Магазин Б", Type = "shop" };
    private readonly Item _bread = new() { TenantId = Tenant, Name = "Хліб", Unit = "шт" };
    private readonly Item _bun = new() { TenantId = Tenant, Name = "Булка", Unit = "шт" };
    private readonly Recipe _recipe;

    public ProductionAnalyticsTests()
    {
        _repo.Locations.AddRange([_prod, _prod2, _shopA, _shopB]);
        _recipe = new Recipe { TenantId = Tenant, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 10m, OutputItem = _bread };
    }

    private ProductionInsightsService Svc() => new(_repo, new FixedClock());
    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private ProductionRequest Req(string status, decimal requested, decimal? accepted, DateTime? submitted,
        DateTime? reviewed = null, DateTime? shipped = null, Location? store = null, Location? prod = null,
        Item? item = null, string? note = null, string? lineReason = null)
    {
        var r = new ProductionRequest
        {
            TenantId = Tenant, StoreLocationId = (store ?? _shopA).Id, StoreLocation = store ?? _shopA,
            ProductionLocationId = (prod ?? _prod).Id, NeededBy = Today, Status = status,
            SubmittedAt = submitted, ReviewedAt = reviewed, ShippedAt = shipped, ReviewNote = note,
        };
        var it = item ?? _bread;
        r.Lines.Add(new ProductionRequestLine
        {
            TenantId = Tenant, RequestId = r.Id, ItemId = it.Id, Item = it,
            RequestedQty = requested, AcceptedQty = accepted, Reason = lineReason,
        });
        _repo.Requests.Add(r);
        return r;
    }

    private ProductionOrder Done(decimal qty, DateTimeOffset completed, DateTimeOffset? started = null, Location? loc = null)
    {
        var o = new ProductionOrder
        {
            TenantId = Tenant, RecipeId = _recipe.Id, Recipe = _recipe, LocationId = (loc ?? _prod).Id,
            PlannedQty = qty, Status = ProductionOrderStatus.Done, StartedAt = started, CompletedAt = completed,
        };
        _repo.Orders.Add(o);
        return o;
    }

    [Fact]
    public async Task Defaults_ToLast30Days_ZeroFilled()
    {
        var a = await Svc().GetAnalyticsAsync(null, null, null);
        Assert.Equal(Today, a.To);
        Assert.Equal(Today.AddDays(-29), a.From);
        Assert.Equal(30, a.PerDay.Count);
        Assert.Equal(Today.AddDays(-59), a.Previous.From);
        Assert.Equal(Today.AddDays(-30), a.Previous.To);
        Assert.Null(a.Totals.FillRate);
        Assert.Null(a.Totals.AvgReviewHours);
        Assert.Null(a.OrdersDone.AvgHours);
    }

    [Fact]
    public async Task OnlyFromOrOnlyTo_IsSingleDay()
    {
        var f = await Svc().GetAnalyticsAsync(Today.AddDays(-2), null, null);
        Assert.Equal((Today.AddDays(-2), Today.AddDays(-2)), (f.From, f.To));
        Assert.Single(f.PerDay);
        var t = await Svc().GetAnalyticsAsync(null, Today.AddDays(-5), null);
        Assert.Equal((Today.AddDays(-5), Today.AddDays(-5)), (t.From, t.To));
        Assert.Equal(Today.AddDays(-6), t.Previous.From);
    }

    [Fact]
    public async Task Totals_FillRate_AndAverageHours()
    {
        // all in local day 10-21 (UTC 10-20T21:00Z .. 10-21T21:00Z)
        // A: submitted 06:00Z, reviewed 08:00Z (2h), accepted 6/10, shipped 12:00Z (6h lead)
        Req(ProductionRequestStatus.Fulfilled, 10m, 6m, Utc(2026, 10, 21, 6), Utc(2026, 10, 21, 8), Utc(2026, 10, 21, 12));
        // B: rejected, 4h review, requested 10
        Req(ProductionRequestStatus.Rejected, 10m, null, Utc(2026, 10, 21, 4), Utc(2026, 10, 21, 8));
        // C: accepted fully 20/20 reviewed in period, submitted before the period (not "received")
        Req(ProductionRequestStatus.Accepted, 20m, 20m, Utc(2026, 10, 19, 10), Utc(2026, 10, 21, 9));
        // D: only submitted, awaiting review
        Req(ProductionRequestStatus.Submitted, 5m, null, Utc(2026, 10, 21, 10));

        var a = await Svc().GetAnalyticsAsync(Today, Today, null);
        var t = a.Totals;
        Assert.Equal(3, t.Received);          // A, B, D
        Assert.Equal(2, t.Accepted);          // A, C
        Assert.Equal(1, t.Rejected);          // B
        Assert.Equal(1, t.Shipped);           // A
        Assert.Equal(25m, t.RequestedQty);    // received: 10 + 10 + 5
        Assert.Equal(26m, t.AcceptedQty);     // 6 + 20
        Assert.Equal(6m, t.ShippedQty);
        Assert.Equal(Math.Round(26m / 40m, 4), t.FillRate); // reviewed requested 10+10+20
        Assert.Equal(Math.Round((2.0 + 4.0 + 47.0) / 3, 2), t.AvgReviewHours);
        Assert.Equal(6.0, t.AvgLeadHours);
    }

    [Fact]
    public async Task OrdersDone_CountQtyAvgHours_AndProducedPerDay()
    {
        Done(40m, new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero));
        Done(20m, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
        Done(5m, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), null);   // no start => ignored by avg
        Done(99m, new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));          // outside
        Done(77m, new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero), null, _prod2); // other location

        var a = await Svc().GetAnalyticsAsync(Today.AddDays(-1), Today, _prod.Id);
        Assert.Equal(3, a.OrdersDone.Count);
        Assert.Equal(65m, a.OrdersDone.Qty);
        Assert.Equal(3.0, a.OrdersDone.AvgHours);
        Assert.Equal(25m, a.PerDay[0].ProducedQty);
        Assert.Equal(40m, a.PerDay[1].ProducedQty);
    }

    [Fact]
    public async Task PerDay_ZeroFilled_AndBoundariesInLocalZone()
    {
        Req(ProductionRequestStatus.Fulfilled, 3m, 3m, Utc(2026, 10, 20, 20, 59), Utc(2026, 10, 20, 20, 59), Utc(2026, 10, 20, 20, 59)); // local 10-20 23:59
        Req(ProductionRequestStatus.Fulfilled, 4m, 4m, Utc(2026, 10, 20, 21, 0), Utc(2026, 10, 20, 21, 0), Utc(2026, 10, 20, 21, 0));   // local 10-21 00:00

        var a = await Svc().GetAnalyticsAsync(Today.AddDays(-2), Today, null);
        Assert.Equal(3, a.PerDay.Count);
        Assert.Equal(Today.AddDays(-2), a.PerDay[0].Date);
        Assert.Equal(0, a.PerDay[0].Received);
        Assert.Equal(1, a.PerDay[1].Received);
        Assert.Equal(1, a.PerDay[1].Shipped);
        Assert.Equal(3m, a.PerDay[1].ShippedQty);
        Assert.Equal(1, a.PerDay[2].Accepted);
        Assert.Equal(4m, a.PerDay[2].ShippedQty);
        Assert.Equal(2, a.Totals.Shipped);
    }

    [Fact]
    public async Task ByItem_ByStore_Aggregate_SortedByShipped()
    {
        Req(ProductionRequestStatus.Fulfilled, 10m, 8m, Utc(2026, 10, 21, 1), Utc(2026, 10, 21, 2), Utc(2026, 10, 21, 5), _shopA, item: _bread);
        Req(ProductionRequestStatus.Fulfilled, 20m, 20m, Utc(2026, 10, 21, 1), Utc(2026, 10, 21, 3), Utc(2026, 10, 21, 7), _shopB, item: _bun);
        Req(ProductionRequestStatus.Rejected, 5m, null, Utc(2026, 10, 21, 1), Utc(2026, 10, 21, 3), null, _shopB, item: _bun);

        var a = await Svc().GetAnalyticsAsync(Today, Today, null);
        Assert.Equal([_bun.Id, _bread.Id], a.ByItem.Select(i => i.ItemId).ToArray());
        var bun = a.ByItem[0];
        Assert.Equal(25m, bun.RequestedQty);
        Assert.Equal(20m, bun.AcceptedQty);
        Assert.Equal(20m, bun.ShippedQty);
        Assert.Equal(0.8m, bun.FillRate); // 20 / (20 + 5)
        Assert.Equal(0.8m, a.ByItem[1].FillRate);

        var b = a.ByStore.Single(s => s.StoreLocationId == _shopB.Id);
        Assert.Equal(2, b.Requests);
        Assert.Equal(25m, b.RequestedQty);
        Assert.Equal(20m, b.ShippedQty);
        Assert.Equal(1, b.Rejected);
        Assert.Equal(6.0, b.AvgLeadHours);
        Assert.Equal(_shopB.Id, a.ByStore[0].StoreLocationId); // more shipped first
        Assert.Equal(4.0, a.ByStore.Single(s => s.StoreLocationId == _shopA.Id).AvgLeadHours);
    }

    [Fact]
    public async Task RejectionReasons_GroupedCaseInsensitive_TrimmedTop10_EmptyExcluded()
    {
        var rv = Utc(2026, 10, 21, 5);
        Req(ProductionRequestStatus.Rejected, 1m, null, rv, rv, note: "  Немає борошна ");
        Req(ProductionRequestStatus.Rejected, 1m, null, rv, rv, note: "немає борошна");
        Req(ProductionRequestStatus.Rejected, 1m, null, rv, rv, note: "   ");
        Req(ProductionRequestStatus.Partial, 10m, 4m, rv, rv, lineReason: "Немає БОРОШНА");
        Req(ProductionRequestStatus.Partial, 10m, 10m, rv, rv, lineReason: "full - ignored");
        Req(ProductionRequestStatus.Partial, 10m, 5m, rv, rv, lineReason: "Мало часу");
        for (var i = 0; i < 12; i++) Req(ProductionRequestStatus.Rejected, 1m, null, rv, rv, note: $"причина {i}");

        var a = await Svc().GetAnalyticsAsync(Today, Today, null);
        Assert.Equal(10, a.RejectionReasons.Count);
        Assert.Equal(3, a.RejectionReasons[0].Count);
        Assert.Equal("Немає борошна", a.RejectionReasons[0].Reason, ignoreCase: true);
        Assert.DoesNotContain(a.RejectionReasons, x => string.IsNullOrWhiteSpace(x.Reason) || x.Reason.StartsWith("full"));
    }

    [Fact]
    public async Task Previous_IsPrecedingWindowOfSameLength()
    {
        var cur = Utc(2026, 10, 21, 5);   // current window 10-20..10-21
        var prev = Utc(2026, 10, 19, 5);  // previous window 10-18..10-19
        var old = Utc(2026, 10, 10, 5);
        Req(ProductionRequestStatus.Fulfilled, 10m, 9m, cur, cur, cur);
        Req(ProductionRequestStatus.Fulfilled, 10m, 7m, prev, prev, prev);
        Req(ProductionRequestStatus.Rejected, 10m, null, prev, prev);
        Req(ProductionRequestStatus.Fulfilled, 10m, 5m, old, old, old);

        var a = await Svc().GetAnalyticsAsync(Today.AddDays(-1), Today, null);
        Assert.Equal(1, a.Totals.Received);
        Assert.Equal(Today.AddDays(-3), a.Previous.From);
        Assert.Equal(Today.AddDays(-2), a.Previous.To);
        Assert.Equal(2, a.Previous.Received);
        Assert.Equal(1, a.Previous.Accepted);
        Assert.Equal(1, a.Previous.Rejected);
        Assert.Equal(1, a.Previous.Shipped);
        Assert.Equal(7m, a.Previous.ShippedQty);
    }

    [Fact]
    public async Task Validation_ThrowsOnReversedOrTooLong()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().GetAnalyticsAsync(Today.AddDays(1), Today, null));
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().GetAnalyticsAsync(Today.AddDays(-366), Today, null));
        var ok = await Svc().GetAnalyticsAsync(Today.AddDays(-365), Today, null);
        Assert.Equal(366, ok.PerDay.Count);
    }

    [Fact]
    public async Task LocationFilter_NarrowsToThatLocation()
    {
        var t = Utc(2026, 10, 21, 5);
        Req(ProductionRequestStatus.Submitted, 1m, null, t, prod: _prod);
        Req(ProductionRequestStatus.Submitted, 1m, null, t, prod: _prod2);
        Req(ProductionRequestStatus.Submitted, 1m, null, t, prod: _prod2);

        Assert.Equal(3, (await Svc().GetAnalyticsAsync(Today, Today, null)).Totals.Received);
        Assert.Equal(1, (await Svc().GetAnalyticsAsync(Today, Today, _prod.Id)).Totals.Received);
        Assert.Equal(2, (await Svc().GetAnalyticsAsync(Today, Today, _prod2.Id)).Totals.Received);
        Assert.Equal(0, (await Svc().GetAnalyticsAsync(Today, Today, _shopA.Id)).Totals.Received);
    }
}
