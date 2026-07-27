using NSubstitute;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PriceSegments;

/// <summary>
/// TASK-420: PriceSegmentsService orchestration — the C#-side aggregation this service does
/// itself (cohort intersection/union building, per-audience grouping, share%/at-risk math,
/// previous-period date computation) that the pure classifiers alone don't exercise, plus the
/// "empty result never throws" and PII-masking/ActivityLog export contracts already established
/// by MarketingAnalyticsServiceTests for Фаза 1. Mocks the repository's raw rows — the actual SQL
/// correctness lives in PriceSegmentsRepositoryIntegrationTests (live Postgres).
/// </summary>
public sealed class PriceSegmentsServiceTests
{
    private readonly IPriceSegmentsRepository _repo = Substitute.For<IPriceSegmentsRepository>();
    private readonly IPriceSegmentAdvisor _advisor = Substitute.For<IPriceSegmentAdvisor>();
    private readonly IExcelExportService _excel = Substitute.For<IExcelExportService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly PriceSegmentsService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    // P20=120 P40=190 P60=280 P80=440 P90=610 P97=960 — same fixture as PriceSegmentClassifierTests.
    private static readonly PriceSegmentBoundariesRow Boundaries = new(120m, 190m, 280m, 440m, 610m, 960m);

    public PriceSegmentsServiceTests()
    {
        _sut = new PriceSegmentsService(_repo, _advisor, _excel, _activityLogs);
        _excel.Export(Arg.Any<ExcelExportRequest>())
            .Returns(ci => new ExcelExportResult([], ci.Arg<ExcelExportRequest>().Rows.Count, false));
    }

    // ── Comparison overview ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverviewAsync_computes_previous_period_as_immediately_preceding_same_length_window()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30); // 30-day window
        var expectedPreviousFrom = new DateOnly(2026, 6, 1);
        var expectedPreviousTo = new DateOnly(2026, 6, 30); // also 30 days, immediately preceding

        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetCustomerPeriodMetricsAsync(TenantId, null, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<PriceCustomerPeriodMetricsRow>());
        _repo.GetAverageUnitPriceAsync(TenantId, null, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(0m);

        await _sut.GetOverviewAsync(TenantId, null, from, to);

        await _repo.Received(1).GetCustomerPeriodMetricsAsync(
            TenantId, null, expectedPreviousFrom, expectedPreviousTo, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetAverageUnitPriceAsync(
            TenantId, null, expectedPreviousFrom, expectedPreviousTo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOverviewAsync_classifies_cohort_and_computes_kpis_correctly()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        var (prevFrom, prevTo) = (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var idA = Guid.NewGuid(); // Tier2 -> Tier5, items 3->4 (up)   => RealGrowth
        var idB = Guid.NewGuid(); // Tier2 -> Tier5, items 3->3 (flat) => PriceGrowth
        var idC = Guid.NewGuid(); // Tier5 -> Tier1                    => Declining
        var idD = Guid.NewGuid(); // Tier2 -> Tier2                    => Stable
        var idOnlyCurrent = Guid.NewGuid(); // no previous-period row -> excluded from cohort

        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);

        _repo.GetCustomerPeriodMetricsAsync(TenantId, null, from, to, Arg.Any<CancellationToken>()).Returns(new List<PriceCustomerPeriodMetricsRow>
        {
            new(idA, MedianCheck: 500m, ReceiptCount: 5, TotalSpend: 2500m, ItemsPerReceipt: 4m),
            new(idB, MedianCheck: 500m, ReceiptCount: 5, TotalSpend: 2500m, ItemsPerReceipt: 3m),
            new(idC, MedianCheck: 100m, ReceiptCount: 5, TotalSpend: 500m, ItemsPerReceipt: 3m),
            new(idD, MedianCheck: 150m, ReceiptCount: 5, TotalSpend: 750m, ItemsPerReceipt: 3m),
            new(idOnlyCurrent, MedianCheck: 500m, ReceiptCount: 1, TotalSpend: 500m, ItemsPerReceipt: 2m),
        });
        _repo.GetCustomerPeriodMetricsAsync(TenantId, null, prevFrom, prevTo, Arg.Any<CancellationToken>()).Returns(new List<PriceCustomerPeriodMetricsRow>
        {
            new(idA, MedianCheck: 150m, ReceiptCount: 5, TotalSpend: 750m, ItemsPerReceipt: 3m),
            new(idB, MedianCheck: 150m, ReceiptCount: 5, TotalSpend: 750m, ItemsPerReceipt: 3m),
            new(idC, MedianCheck: 500m, ReceiptCount: 5, TotalSpend: 2500m, ItemsPerReceipt: 3m),
            new(idD, MedianCheck: 150m, ReceiptCount: 5, TotalSpend: 750m, ItemsPerReceipt: 3m),
        });
        _repo.GetAverageUnitPriceAsync(TenantId, null, from, to, Arg.Any<CancellationToken>()).Returns(110m);
        _repo.GetAverageUnitPriceAsync(TenantId, null, prevFrom, prevTo, Arg.Any<CancellationToken>()).Returns(100m);
        // NSubstitute requires a matcher (not a mix of matcher + literal) for every argument of
        // the same type in one call — both customerIds and storeIds are IReadOnlyList<Guid>?.
        _repo.GetLtvMapAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PriceLtvRow>());

        var result = await _sut.GetOverviewAsync(TenantId, null, from, to);

        Assert.Equal(4, result.AnalyzedCount);
        Assert.Equal(5, result.CurrentPeriodBuyerCount);
        Assert.Equal(4, result.PreviousPeriodBuyerCount);
        Assert.Equal(2, result.RaisedCount);
        Assert.Equal(1, result.DeclinedCount);
        Assert.Equal(1, result.StableCount);
        Assert.Equal(10m, result.PriceIndexPercent);
        Assert.Equal(7, result.Distribution.Count); // always all 7 tiers, even empty ones

        Assert.Equal(1, result.Audiences.Single(a => a.Audience == PriceAudienceKey.RealGrowth).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == PriceAudienceKey.PriceGrowth).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == PriceAudienceKey.Declining).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == PriceAudienceKey.Stable).CustomerCount);
    }

    // ── Comparison audience table ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAudienceTableAsync_maps_rows_and_uses_window_aggregates_for_recommendation()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);

        var custId = Guid.NewGuid();
        _repo.GetPriceAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Boundaries,
                (int)PriceAudienceKey.Declining, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<PriceAudienceTableRowRaw>
            {
                new(custId, "Іван", "+380671112233", 5, 1, 3m, 3m, 100m, 5000m,
                    TotalCount: 42, WithPhoneCount: 40, AnalyzedCount: 100, AverageLtvOverall: 5000m),
            });

        var request = new PriceAudienceTableRequest(PriceAudienceKey.Declining, from, to, null, 1, 20, null, false);
        var result = await _sut.GetAudienceTableAsync(TenantId, request);

        Assert.Equal(42, result.TotalCount);
        Assert.Equal(40, result.WithPhoneCount);
        var row = Assert.Single(result.Rows);
        Assert.Equal(custId, row.CustomerId);
        Assert.Equal(PriceSegmentKey.Tier5, row.PreviousSegment); // ordinal 5
        Assert.Equal(PriceSegmentKey.Tier1, row.CurrentSegment);
        Assert.Equal("check", result.SortBy); // unrecognized/omitted sortBy normalizes to the default
        Assert.False(string.IsNullOrWhiteSpace(result.Recommendation.TriggerUa));
    }

    [Fact]
    public async Task GetAudienceTableAsync_empty_result_never_throws_and_zeroes_kpis()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetPriceAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Boundaries,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<PriceAudienceTableRowRaw>());

        var request = new PriceAudienceTableRequest(PriceAudienceKey.RealGrowth, from, to, null, 1, 20, null, false);
        var result = await _sut.GetAudienceTableAsync(TenantId, request);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.NotNull(result.Recommendation);
    }

    // ── Frequency overview ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFrequencyOverviewAsync_classifies_union_population_and_reports_both_at_risk_denominators()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        var (prevFrom, prevTo) = (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var sleeping = Guid.NewGuid();    // previous=5, current=0
        var declining = Guid.NewGuid();   // previous=10, current=5 -> 50% decline >= 30% threshold
        var growing = Guid.NewGuid();     // previous=2, current=6
        var stableOther = Guid.NewGuid(); // previous=3, current=3

        _repo.GetSettingsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((PriceSegmentSettings?)null);

        _repo.GetCustomerPeriodMetricsAsync(TenantId, null, from, to, Arg.Any<CancellationToken>()).Returns(new List<PriceCustomerPeriodMetricsRow>
        {
            new(declining, 100m, 5, 500m, 3m),
            new(growing, 100m, 6, 600m, 3m),
            new(stableOther, 100m, 3, 300m, 3m),
        });
        _repo.GetCustomerPeriodMetricsAsync(TenantId, null, prevFrom, prevTo, Arg.Any<CancellationToken>()).Returns(new List<PriceCustomerPeriodMetricsRow>
        {
            new(sleeping, 100m, 5, 500m, 3m),
            new(declining, 100m, 10, 1000m, 3m),
            new(growing, 100m, 2, 200m, 3m),
            new(stableOther, 100m, 3, 300m, 3m),
        });
        _repo.GetLtvMapAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PriceLtvRow>());

        var result = await _sut.GetFrequencyOverviewAsync(TenantId, null, from, to, declineThresholdPercent: null);

        Assert.Equal(4, result.UnionPopulationCount);
        Assert.Equal(3, result.ActiveCurrentBuyerCount);
        Assert.Equal(2, result.AtRiskCount); // sleeping + declining
        Assert.Equal(50m, result.AtRiskPercentOfUnionPopulation); // 2 / 4
        Assert.Equal(66.67m, result.AtRiskPercentOfActiveCurrentBuyers); // 2 / 3

        Assert.Equal(1, result.Audiences.Single(a => a.Audience == FrequencyAudienceKey.Sleeping).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == FrequencyAudienceKey.Declining).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == FrequencyAudienceKey.Growing).CustomerCount);
        Assert.Equal(1, result.Audiences.Single(a => a.Audience == FrequencyAudienceKey.Other).CustomerCount);
    }

    // ── Settings (lazy defaults) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSettingsAsync_returns_proposed_defaults_when_tenant_never_saved_a_row()
    {
        _repo.GetSettingsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((PriceSegmentSettings?)null);

        var result = await _sut.GetSettingsAsync(TenantId);

        Assert.Equal(30.0m, result.DefaultFrequencyDeclineThresholdPercent);
        Assert.Null(result.MinReceiptsForBoundaries);
        Assert.Null(result.UpdatedAt); // never-saved -> no real UpdatedAt to report
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(101, null)]
    [InlineData(50, -5)]
    public async Task UpsertSettingsAsync_rejects_invalid_input(double threshold, int? minReceipts)
    {
        var request = new UpsertPriceSegmentSettingsRequest((decimal)threshold, minReceipts);

        var (dto, error) = await _sut.UpsertSettingsAsync(TenantId, request);

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpsertSettingsAsync_creates_new_row_when_none_exists()
    {
        _repo.GetSettingsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((PriceSegmentSettings?)null);
        var request = new UpsertPriceSegmentSettingsRequest(25m, 5);

        var (dto, error) = await _sut.UpsertSettingsAsync(TenantId, request);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(25m, dto!.DefaultFrequencyDeclineThresholdPercent);
        Assert.Equal(5, dto.MinReceiptsForBoundaries);
        Assert.NotNull(dto.UpdatedAt);
        await _repo.Received(1).AddSettingsAsync(Arg.Any<PriceSegmentSettings>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Export: PII masking + ActivityLog audit ──────────────────────────────────────────────

    [Fact]
    public async Task ExportAudienceAsync_masks_phone_by_default_and_writes_activity_log()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);

        var rows = new List<PriceAudienceTableRowRaw>
        {
            new(Guid.NewGuid(), "Іван", "+380671112233", 1, 2, 3m, 3m, 200m, 1000m,
                TotalCount: 1, WithPhoneCount: 1, AnalyzedCount: 1, AverageLtvOverall: 1000m),
        };
        _repo.GetPriceAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Boundaries,
                Arg.Any<int>(), 1, Arg.Any<int>(), null, false, Arg.Any<CancellationToken>())
            .Returns(rows);

        ExcelExportRequest? captured = null;
        _excel.Export(Arg.Do<ExcelExportRequest>(r => captured = r))
            .Returns(new ExcelExportResult([], 1, false));

        var request = new ExportPriceAudienceRequest(PriceAudienceKey.RealGrowth, from, to, null, UnmaskPii: false);
        var userId = Guid.NewGuid();
        await _sut.ExportAudienceAsync(TenantId, userId, request);

        Assert.NotNull(captured);
        var phoneCell = captured!.Rows[0][1] as string;
        Assert.NotEqual("+380671112233", phoneCell);

        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.TenantId == TenantId && l.UserId == userId && l.Action.Contains("export_audience")),
            Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── On-screen PII masking, GET tables (TASK-425) ─────────────────────────────────────────
    // QA (TASK-424) found all 3 paginated GET-table endpoints returned Phone unmasked
    // unconditionally — masking previously existed only inside the 3 Excel export builders
    // above. These prove the fix: masked by default (no capability), full number only when the
    // controller resolves CanViewUnmaskedPii=true (i.e. MarketingAnalyticsAuthorization.
    // CanExportPii(User) was true) — same gate the export path already used.

    private const string RawPhone = "+380671112233";

    [Fact]
    public async Task GetAudienceTableAsync_masks_phone_on_screen_by_default()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetPriceAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Boundaries,
                (int)PriceAudienceKey.Declining, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<PriceAudienceTableRowRaw>
            {
                new(Guid.NewGuid(), "Іван", RawPhone, 5, 1, 3m, 3m, 100m, 5000m,
                    TotalCount: 1, WithPhoneCount: 1, AnalyzedCount: 1, AverageLtvOverall: 5000m),
            });

        var request = new PriceAudienceTableRequest(PriceAudienceKey.Declining, from, to, null, 1, 20, null, false);
        var result = await _sut.GetAudienceTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.NotEqual(RawPhone, row.Phone);
        Assert.Equal("+380 67 *** ** 33", row.Phone);
    }

    [Fact]
    public async Task GetAudienceTableAsync_reveals_full_phone_when_caller_can_view_unmasked_pii()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetPriceAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Boundaries,
                (int)PriceAudienceKey.Declining, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<PriceAudienceTableRowRaw>
            {
                new(Guid.NewGuid(), "Іван", RawPhone, 5, 1, 3m, 3m, 100m, 5000m,
                    TotalCount: 1, WithPhoneCount: 1, AnalyzedCount: 1, AverageLtvOverall: 5000m),
            });

        var request = new PriceAudienceTableRequest(
            PriceAudienceKey.Declining, from, to, null, 1, 20, null, false, CanViewUnmaskedPii: true);
        var result = await _sut.GetAudienceTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.Equal(RawPhone, row.Phone);
    }

    [Fact]
    public async Task GetAllTimeCustomerTableAsync_masks_phone_on_screen_by_default()
    {
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetAllTimeCustomerTableAsync(
                TenantId, null, Boundaries, (int?)null, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<AllTimeCustomerRowRaw>
            {
                new(Guid.NewGuid(), "Марія", RawPhone, 3, 4m, 250m, 10, 3000m, TotalCount: 1),
            });

        var request = new AllTimeCustomerTableRequest(null, null, 1, 20, null, false);
        var result = await _sut.GetAllTimeCustomerTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.NotEqual(RawPhone, row.Phone);
        Assert.Equal("+380 67 *** ** 33", row.Phone);
    }

    [Fact]
    public async Task GetAllTimeCustomerTableAsync_reveals_full_phone_when_caller_can_view_unmasked_pii()
    {
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetAllTimeCustomerTableAsync(
                TenantId, null, Boundaries, (int?)null, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<AllTimeCustomerRowRaw>
            {
                new(Guid.NewGuid(), "Марія", RawPhone, 3, 4m, 250m, 10, 3000m, TotalCount: 1),
            });

        var request = new AllTimeCustomerTableRequest(null, null, 1, 20, null, false, CanViewUnmaskedPii: true);
        var result = await _sut.GetAllTimeCustomerTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.Equal(RawPhone, row.Phone);
    }

    [Fact]
    public async Task GetFrequencyAudienceTableAsync_masks_phone_on_screen_by_default()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetSettingsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((PriceSegmentSettings?)null);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetFrequencyAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                30.0m, Boundaries, (int)FrequencyAudienceKey.Declining, true,
                null, null, (int?)null, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<FrequencyAudienceTableRowRaw>
            {
                new(Guid.NewGuid(), "Петро", RawPhone, 10, 5, -5, -50m, 120m, 600m, 3000m,
                    TotalCount: 1, WithPhoneCount: 1, UnionPopulationCount: 4,
                    AverageLtvOverall: 3000m, AveragePreviousFrequencyOverall: 10m, AverageCurrentFrequencyOverall: 5m),
            });

        var request = new FrequencyAudienceTableRequest(
            FrequencyAudienceKey.Declining, from, to, null, null, null, null, null, 1, 20, null, false);
        var result = await _sut.GetFrequencyAudienceTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.NotEqual(RawPhone, row.Phone);
        Assert.Equal("+380 67 *** ** 33", row.Phone);
    }

    [Fact]
    public async Task GetFrequencyAudienceTableAsync_reveals_full_phone_when_caller_can_view_unmasked_pii()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 30);
        _repo.GetSettingsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((PriceSegmentSettings?)null);
        _repo.GetBoundariesAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(Boundaries);
        _repo.GetFrequencyAudienceTableAsync(
                TenantId, null, from, to, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                30.0m, Boundaries, (int)FrequencyAudienceKey.Declining, true,
                null, null, (int?)null, 1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns(new List<FrequencyAudienceTableRowRaw>
            {
                new(Guid.NewGuid(), "Петро", RawPhone, 10, 5, -5, -50m, 120m, 600m, 3000m,
                    TotalCount: 1, WithPhoneCount: 1, UnionPopulationCount: 4,
                    AverageLtvOverall: 3000m, AveragePreviousFrequencyOverall: 10m, AverageCurrentFrequencyOverall: 5m),
            });

        var request = new FrequencyAudienceTableRequest(
            FrequencyAudienceKey.Declining, from, to, null, null, null, null, null, 1, 20, null, false,
            CanViewUnmaskedPii: true);
        var result = await _sut.GetFrequencyAudienceTableAsync(TenantId, request);

        var row = Assert.Single(result.Rows);
        Assert.Equal(RawPhone, row.Phone);
    }
}
