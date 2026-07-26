using NSubstitute;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics;

/// <summary>
/// TASK-406: MarketingAnalyticsService orchestration — classification-to-DTO aggregation math
/// (share%, no-purchase card), the "empty segment never throws" QA requirement, and the
/// PII-masking/ActivityLog contract on exports. RfmSegmentClassifier's own rule coverage lives
/// in RfmSegmentClassifierTests — these tests mock the repository's already-scored rows rather
/// than re-deriving segment membership from raw R/F/M inputs.
/// </summary>
public sealed class MarketingAnalyticsServiceTests
{
    private readonly IMarketingAnalyticsRepository _repo = Substitute.For<IMarketingAnalyticsRepository>();
    private readonly IMarketingAdvisor _advisor = Substitute.For<IMarketingAdvisor>();
    private readonly IExcelExportService _excel = Substitute.For<IExcelExportService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly MarketingAnalyticsService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    public MarketingAnalyticsServiceTests()
    {
        _sut = new MarketingAnalyticsService(_repo, _advisor, _excel, _activityLogs);
        _excel.Export(Arg.Any<ExcelExportRequest>())
            .Returns(ci => new ExcelExportResult(
                [], ci.Arg<ExcelExportRequest>().Rows.Count, false));
    }

    // A "Champions" row (R=5,F=5,M=5) and a "Hibernating" row (R=1,F=1,M=1) — both simple,
    // unambiguous, single-rule matches so this test doesn't need to re-derive classification
    // edge cases already covered by RfmSegmentClassifierTests.
    private static RfmScoredCustomerRow ChampionRow(Guid customerId, decimal revenue) => new(
        customerId, RecencyScore: 5, FrequencyScore: 5, MonetaryScore: 5,
        PeriodRevenue: revenue, PeriodReceiptCount: 10,
        FirstPurchaseDateEver: From.AddDays(-100), LifetimeReceiptCount: 20, LastPurchaseDateEver: To.AddDays(-1));

    private static RfmScoredCustomerRow HibernatingRow(Guid customerId, decimal revenue) => new(
        customerId, RecencyScore: 1, FrequencyScore: 1, MonetaryScore: 1,
        PeriodRevenue: revenue, PeriodReceiptCount: 1,
        FirstPurchaseDateEver: From.AddDays(-500), LifetimeReceiptCount: 5, LastPurchaseDateEver: To.AddDays(-200));

    [Fact]
    public async Task GetOverviewAsync_returns_all_11_segments_even_when_most_are_empty()
    {
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>())
            .Returns([ChampionRow(Guid.NewGuid(), 1000m)]);
        _repo.GetCustomerBaseCountsAsync(TenantId, null, Arg.Any<CancellationToken>())
            .Returns(new RfmCustomerBaseCountsRow(RegisteredCount: 1, EverPurchasedCount: 1));

        var result = await _sut.GetOverviewAsync(TenantId, null, From, To);

        Assert.Equal(11, result.Segments.Count);
        Assert.Equal(RfmSegmentCatalog.AllKeysInPriorityOrder, result.Segments.Select(s => s.Key));
        var champions = result.Segments.Single(s => s.Key == RfmSegmentKey.Champions);
        Assert.Equal(1, champions.CustomerCount);
        Assert.Equal(100m, champions.SharePercentOfPeriodCustomers); // 1 of 1 = 100%
        foreach (var empty in result.Segments.Where(s => s.Key != RfmSegmentKey.Champions))
        {
            Assert.Equal(0, empty.CustomerCount);
            Assert.Equal(0m, empty.SharePercentOfPeriodCustomers);
            Assert.Equal(0m, empty.Revenue);
        }
    }

    [Fact]
    public async Task GetOverviewAsync_computes_revenue_shares_and_no_purchase_card_correctly()
    {
        var championId = Guid.NewGuid();
        var hibernatingId = Guid.NewGuid();
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>())
            .Returns([ChampionRow(championId, 750m), HibernatingRow(hibernatingId, 250m)]);
        // 5 registered total: the 2 who purchased in-window + 3 more who never purchased at all.
        _repo.GetCustomerBaseCountsAsync(TenantId, null, Arg.Any<CancellationToken>())
            .Returns(new RfmCustomerBaseCountsRow(RegisteredCount: 5, EverPurchasedCount: 2));

        var result = await _sut.GetOverviewAsync(TenantId, null, From, To);

        Assert.Equal(2, result.PeriodCustomerCount);
        Assert.Equal(1000m, result.PeriodRevenue);
        Assert.Equal(5, result.RegisteredCustomerCount);
        Assert.Equal(2, result.EverPurchasedCustomerCount);
        Assert.Equal(40m, result.EverPurchasedSharePercent); // 2 of 5

        var champions = result.Segments.Single(s => s.Key == RfmSegmentKey.Champions);
        Assert.Equal(75m, champions.SharePercentOfPeriodRevenue); // 750 of 1000
        var hibernating = result.Segments.Single(s => s.Key == RfmSegmentKey.Hibernating);
        Assert.Equal(25m, hibernating.SharePercentOfPeriodRevenue); // 250 of 1000

        Assert.Equal(3, result.NoPurchase.CustomerCount); // 5 registered - 2 ever purchased
        Assert.Equal(60m, result.NoPurchase.SharePercentOfRegisteredBase); // 3 of 5
    }

    [Fact]
    public async Task GetOverviewAsync_handles_zero_registered_customers_without_dividing_by_zero()
    {
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetCustomerBaseCountsAsync(TenantId, null, Arg.Any<CancellationToken>())
            .Returns(new RfmCustomerBaseCountsRow(RegisteredCount: 0, EverPurchasedCount: 0));

        var result = await _sut.GetOverviewAsync(TenantId, null, From, To);

        Assert.Equal(0, result.PeriodCustomerCount);
        Assert.Equal(0m, result.EverPurchasedSharePercent);
        Assert.Equal(0, result.NoPurchase.CustomerCount);
        Assert.Equal(0m, result.NoPurchase.SharePercentOfRegisteredBase);
        Assert.All(result.Segments, s => Assert.Equal(0, s.CustomerCount));
    }

    [Fact]
    public async Task GetSegmentDetailAsync_on_an_empty_segment_never_throws_and_returns_zeroed_dto()
    {
        // Only a Champion is scored — every other segment (e.g. Lost, requested below) is empty.
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>())
            .Returns([ChampionRow(Guid.NewGuid(), 500m)]);

        var detail = await _sut.GetSegmentDetailAsync(TenantId, null, From, To, RfmSegmentKey.Lost);

        Assert.Equal(0, detail.CustomerCount);
        Assert.Empty(detail.TopProducts);
        Assert.Equal(0, detail.Behavior.ReceiptCount);
        Assert.Null(detail.Behavior.LastVisit);
        Assert.NotNull(detail.Recommendation); // template still generates copy for a 0-count segment

        // Repository must never be asked for top-products/behavior/LTV on an empty customer list.
        await _repo.DidNotReceive().GetTopProductsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(),
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportSegmentAsync_masks_phone_and_email_by_default_and_unmasks_when_requested()
    {
        var customerId = Guid.NewGuid();
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>())
            .Returns([ChampionRow(customerId, 500m)]);
        _repo.GetExportCustomersAsync(TenantId, Arg.Is<IReadOnlyList<Guid>>(l => l.Contains(customerId)), Arg.Any<CancellationToken>())
            .Returns([new RfmExportCustomerRow(customerId, "Іван Тест", "+380671234567", "ivan@test.com", 5, 1234.50m)]);

        var userId = Guid.NewGuid();

        await _sut.ExportSegmentAsync(TenantId, userId, new ExportRfmFilterRequest(RfmSegmentKey.Champions, From, To, null, UnmaskPii: false));
        var maskedCall = _excel.ReceivedCalls().Last();
        var maskedRequest = (ExcelExportRequest)maskedCall.GetArguments()[0]!;
        Assert.Equal("+380 67 *** ** 67", maskedRequest.Rows[0][1]);
        // TASK-414 (security review TASK-412, finding C6b): email used to be written verbatim
        // regardless of unmaskPii — inconsistent with phone's own "masked by default" behavior
        // right above. Now masked the same way.
        Assert.Equal("i***@test.com", maskedRequest.Rows[0][2]);

        await _sut.ExportSegmentAsync(TenantId, userId, new ExportRfmFilterRequest(RfmSegmentKey.Champions, From, To, null, UnmaskPii: true));
        var unmaskedCall = _excel.ReceivedCalls().Last();
        var unmaskedRequest = (ExcelExportRequest)unmaskedCall.GetArguments()[0]!;
        Assert.Equal("+380671234567", unmaskedRequest.Rows[0][1]);
        Assert.Equal("ivan@test.com", unmaskedRequest.Rows[0][2]);

        // Every export writes an audit trail entry (Action + Meta snapshot), per the brief.
        await _activityLogs.Received(2).LogAsync(
            Arg.Is<ActivityLog>(a => a.TenantId == TenantId && a.UserId == userId && a.Action == "marketing_analytics.export_segment"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExplainSegmentAsync_feeds_the_advisor_the_same_kpis_and_template_text_as_the_detail_dto()
    {
        var customerId = Guid.NewGuid();
        _repo.GetScoredCustomersAsync(TenantId, null, From, To, Arg.Any<CancellationToken>())
            .Returns([ChampionRow(customerId, 500m)]);
        // A non-empty segment (customerIds.Count > 0) makes BuildSegmentDetailAsync call these
        // three — a real MarketingAnalyticsRepository never returns null (its interface return
        // types are non-nullable), so these must be stubbed with real (if empty) values rather
        // than left as NSubstitute's default null for an unconfigured reference-type return.
        _repo.GetTopProductsAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(), From, To, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.GetBehaviorAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(), From, To, Arg.Any<CancellationToken>())
            .Returns(new RfmBehaviorRow(0, 0m, null, 0m, [], []));
        _repo.GetLtvAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new RfmLtvRow(0m, 0m));
        _advisor.ExplainAsync(Arg.Any<MarketingAdvisorContext>(), Arg.Any<CancellationToken>())
            .Returns(new MarketingAdvisorResult("Пояснення", "claude-sonnet-4-6", 123));

        var result = await _sut.ExplainSegmentAsync(TenantId, null, From, To, RfmSegmentKey.Champions);

        Assert.Equal("Пояснення", result.ExplanationUa);
        Assert.Equal(123, result.TokensUsed);

        await _advisor.Received(1).ExplainAsync(
            Arg.Is<MarketingAdvisorContext>(c => c.SegmentKey == RfmSegmentKey.Champions && c.CustomerCount == 1),
            Arg.Any<CancellationToken>());
    }
}
