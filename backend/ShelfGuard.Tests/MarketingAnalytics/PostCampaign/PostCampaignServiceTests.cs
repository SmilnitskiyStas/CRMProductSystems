using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PostCampaign;

/// <summary>
/// TASK-472: PostCampaignService orchestration, mocked repositories (NSubstitute) — the import
/// resolve/dedup pipeline, the analyze window formula, the not-found/not-analyzed error
/// convention, the RFM migration matrix's marginal-sum identity (using synthetic
/// <see cref="RfmScoredCustomerRow"/> data — the classifier itself is pure, so this needs no live
/// Postgres), PII masking in the customers table, and the export/ActivityLog audit contract. Live-
/// DB correctness of the repository's own EF Core LINQ queries and an end-to-end migration-matrix
/// identity against REAL NTILE scoring live in <c>PostCampaignRepositoryIntegrationTests</c>.
/// </summary>
public sealed class PostCampaignServiceTests
{
    private readonly IPostCampaignRepository _repo = Substitute.For<IPostCampaignRepository>();
    private readonly IMarketingAnalyticsRepository _marketingAnalytics = Substitute.For<IMarketingAnalyticsRepository>();
    private readonly IPostCampaignAdvisor _advisor = Substitute.For<IPostCampaignAdvisor>();
    private readonly IExcelExportService _excelExport = Substitute.For<IExcelExportService>();
    private readonly IExcelImportService _excelImport = Substitute.For<IExcelImportService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly PostCampaignService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public PostCampaignServiceTests()
    {
        _sut = new PostCampaignService(_repo, _marketingAnalytics, _advisor, _excelExport, _excelImport, _activityLogs);
        _excelExport.Export(Arg.Any<ExcelExportRequest>())
            .Returns(ci => new ExcelExportResult([], ci.Arg<ExcelExportRequest>().Rows.Count, false));
    }

    private static PostCampaignSegment AnalyzedSegment(
        Guid? id = null, DateOnly? afterStart = null, DateOnly? afterEnd = null,
        DateOnly? beforeStart = null, DateOnly? beforeEnd = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = TenantId,
        CreatedByUserId = UserId,
        AfterStart = afterStart ?? new DateOnly(2026, 5, 1),
        AfterEnd = afterEnd ?? new DateOnly(2026, 5, 7),
        BeforeStart = beforeStart ?? new DateOnly(2026, 4, 24),
        BeforeEnd = beforeEnd ?? new DateOnly(2026, 4, 30),
        SegmentHash = "abc123",
        AnalyzedAt = DateTimeOffset.UtcNow,
    };

    // ── Import ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_matches_valid_guid_tokens_against_the_repository_and_creates_members()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _repo.FindCustomersByIdsOrPhonesAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new MatchedCustomerRow(c1, null), new MatchedCustomerRow(c2, null)]);

        var request = new PostCampaignImportRequest($"{c1:D}\n{c2:D}", null, null, "My segment", null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(2, result!.UploadedCount);
        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(0, result.UnknownCount);
        Assert.Equal(0, result.DuplicateCount);

        await _repo.Received(1).AddMembersAsync(
            Arg.Is<IReadOnlyList<PostCampaignSegmentMember>>(m => m.Count == 2), Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a => a.Action == "marketing_analytics.post_campaign.import"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_counts_a_well_formed_token_that_matches_no_customer_as_unknown()
    {
        _repo.FindCustomersByIdsOrPhonesAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var unknownGuid = Guid.NewGuid();
        var request = new PostCampaignImportRequest(unknownGuid.ToString("D"), null, null, null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(error);
        Assert.Equal(1, result!.UnknownCount);
        Assert.Equal(0, result.MatchedCount);
        Assert.Contains(unknownGuid.ToString("D"), result.UnknownTokensSample);
    }

    [Fact]
    public async Task ImportAsync_collapses_two_different_tokens_resolving_to_the_same_customer_into_one_member()
    {
        // A GUID token and that same customer's phone token both submitted — must produce
        // exactly ONE PostCampaignSegmentMember row, with the extra counted as a duplicate (not
        // a second match) so MatchedCount always equals the real inserted-row count.
        var customerId = Guid.NewGuid();
        _repo.FindCustomersByIdsOrPhonesAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new MatchedCustomerRow(customerId, "+380671234567")]);

        var request = new PostCampaignImportRequest($"{customerId:D}\n0671234567", null, null, null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(error);
        Assert.Equal(2, result!.UploadedCount);
        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.DuplicateCount);

        await _repo.Received(1).AddMembersAsync(
            Arg.Is<IReadOnlyList<PostCampaignSegmentMember>>(m => m.Count == 1 && m[0].CustomerId == customerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_rejects_a_submission_over_the_row_cap_without_touching_the_repository()
    {
        var manyTokens = string.Join('\n', Enumerable.Range(0, PostCampaignService.MaxAcceptedRows + 1).Select(_ => Guid.NewGuid().ToString("D")));
        var request = new PostCampaignImportRequest(manyTokens, null, null, null, null);

        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_requires_exactly_one_of_rawText_or_file()
    {
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, new PostCampaignImportRequest(null, null, null, null, null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ── Import: TASK-474/477 security fixes (findings A/C) ──────────────────────────────────────

    [Fact]
    public async Task ImportAsync_translates_an_oversized_xlsx_into_a_clean_error_without_touching_the_repository()
    {
        // ExcelImportServiceTests covers the REAL ceiling logic inside ExcelImportService.ParseXlsx;
        // this covers ONLY that PostCampaignService correctly translates the exception it throws.
        _excelImport.ParseXlsx(Arg.Any<Stream>()).Throws(new ImportTooLargeException("too many rows"));

        var request = new PostCampaignImportRequest(null, [1, 2, 3], "big.xlsx", null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_translates_a_FormatException_from_the_excel_import_service_into_a_clean_error()
    {
        _excelImport.ParseXlsx(Arg.Any<Stream>()).Throws(new FormatException("bad workbook"));

        var request = new PostCampaignImportRequest(null, [1, 2, 3], "corrupt.xlsx", null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_translates_a_NullReferenceException_from_the_excel_import_service_into_a_clean_error()
    {
        // ExcelImportServiceTests pins the REAL ClosedXML behavior this mirrors: a well-formed
        // zip that is not a valid xlsx package throws a bare NullReferenceException from inside
        // ClosedXML's own LoadSpreadsheetDocument — must not reach the caller as an unhandled 500.
        _excelImport.ParseXlsx(Arg.Any<Stream>()).Throws(new NullReferenceException());

        var request = new PostCampaignImportRequest(null, [1, 2, 3], "corrupt.xlsx", null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_rejects_an_oversized_raw_text_paste_before_the_generic_ceiling()
    {
        // Defense-in-depth (finding A): exceeds ImportLimits.MaxRows itself (not just the lower,
        // friendlier MaxAcceptedRows already covered above) — proves SegmentImportParser.ParseTextList's
        // own early-exit guard, not just PostCampaignService's later MaxAcceptedRows check.
        var manyTokens = string.Join(',', Enumerable.Range(0, ImportLimits.MaxRows + 1).Select(i => i.ToString()));
        var request = new PostCampaignImportRequest(manyTokens, null, null, null, null);

        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_rejects_an_oversized_csv_file_before_the_generic_ceiling()
    {
        // Real (unmocked) SegmentImportParser.ParseCsvText call — a ".csv" filename never reaches
        // the mocked _excelImport at all, so this exercises the CSV early-exit guard end to end.
        var lines = new List<string> { "id" };
        lines.AddRange(Enumerable.Range(0, ImportLimits.MaxRows).Select(i => i.ToString()));
        var csvBytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));

        var request = new PostCampaignImportRequest(null, csvBytes, "big.csv", null, null);
        var (result, error) = await _sut.ImportAsync(TenantId, UserId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        _excelImport.DidNotReceive().ParseXlsx(Arg.Any<Stream>());
        await _repo.DidNotReceive().AddSegmentAsync(Arg.Any<PostCampaignSegment>(), Arg.Any<CancellationToken>());
    }

    // ── Analyze ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_returns_not_found_error_when_segment_does_not_exist()
    {
        _repo.GetSegmentAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PostCampaignSegment?)null);

        var (result, error) = await _sut.AnalyzeAsync(TenantId, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7));

        Assert.Null(result);
        Assert.EndsWith("not found.", error);
    }

    [Fact]
    public async Task AnalyzeAsync_rejects_afterStart_later_than_afterEnd_without_not_found_wording()
    {
        var segment = new PostCampaignSegment { TenantId = TenantId, CreatedByUserId = UserId };
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);

        var (result, error) = await _sut.AnalyzeAsync(TenantId, segment.Id, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 1));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.DoesNotContain("not found.", error);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(1)]
    public async Task AnalyzeAsync_computes_an_equal_length_immediately_preceding_before_window(int windowDays)
    {
        var segment = new PostCampaignSegment { TenantId = TenantId, CreatedByUserId = UserId };
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);

        var afterStart = new DateOnly(2026, 7, 11);
        var afterEnd = afterStart.AddDays(windowDays - 1);

        var (result, error) = await _sut.AnalyzeAsync(TenantId, segment.Id, afterStart, afterEnd);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(afterStart.AddDays(-1), result!.BeforeEnd);
        Assert.Equal(afterStart.AddDays(-windowDays), result.BeforeStart);
        // Both windows are exactly the same length.
        Assert.Equal(afterEnd.DayNumber - afterStart.DayNumber, result.BeforeEnd.DayNumber - result.BeforeStart.DayNumber);
        Assert.NotEqual(string.Empty, result.SegmentHash);
        Assert.Equal(segment.SegmentHash, result.SegmentHash);
        Assert.NotNull(segment.AnalyzedAt);
    }

    // ── Report endpoints: not-found / not-analyzed guard ────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_returns_not_found_when_segment_does_not_exist()
    {
        _repo.GetSegmentAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PostCampaignSegment?)null);

        var (result, error) = await _sut.GetSummaryAsync(TenantId, Guid.NewGuid(), null);

        Assert.Null(result);
        Assert.EndsWith("not found.", error);
    }

    [Fact]
    public async Task GetSummaryAsync_rejects_a_draft_unanalyzed_segment_without_not_found_wording()
    {
        var draft = new PostCampaignSegment { TenantId = TenantId, CreatedByUserId = UserId }; // AnalyzedAt null
        _repo.GetSegmentAsync(TenantId, draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var (result, error) = await _sut.GetSummaryAsync(TenantId, draft.Id, null);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.DoesNotContain("not found.", error);
    }

    [Fact]
    public async Task GetSummaryAsync_handles_a_zero_member_segment_without_throwing()
    {
        var segment = AnalyzedSegment();
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns([]);

        var (result, error) = await _sut.GetSummaryAsync(TenantId, segment.Id, null);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(0, result!.MatchedCount);
        Assert.Equal(0m, result.MoneyAfter);
        Assert.Null(result.ReactivationRatePercent); // no inactive-before at all -> null, not 0%
    }

    // ── Summary: behavior classification + balance identity ─────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_classifies_every_member_and_satisfies_the_balance_identity()
    {
        var segment = AnalyzedSegment();
        var reactivatedId = Guid.NewGuid();
        var retainedId = Guid.NewGuid();
        var droppedId = Guid.NewGuid();
        var notReturnedId = Guid.NewGuid();
        var customerIds = new List<Guid> { reactivatedId, retainedId, droppedId, notReturnedId };

        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(customerIds);

        // Before window: retained + dropped bought; reactivated + notReturned did not.
        _repo.GetCustomerPeriodMetricsAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(),
                Arg.Is<DateTime>(d => d.Date == segment.BeforeStart!.Value.ToDateTime(TimeOnly.MinValue).Date), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new CustomerPeriodMetricsRow(retainedId, 2, 500m, DateTime.UtcNow),
                new CustomerPeriodMetricsRow(droppedId, 1, 300m, DateTime.UtcNow),
            ]);
        // After window: reactivated + retained bought; dropped + notReturned did not.
        _repo.GetCustomerPeriodMetricsAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(),
                Arg.Any<DateTime>(), Arg.Is<DateTime>(d => d.Date == segment.AfterEnd!.Value.ToDateTime(TimeOnly.MaxValue).Date), Arg.Any<CancellationToken>())
            .Returns(
            [
                new CustomerPeriodMetricsRow(reactivatedId, 1, 200m, DateTime.UtcNow),
                new CustomerPeriodMetricsRow(retainedId, 3, 600m, DateTime.UtcNow),
            ]);

        var (result, error) = await _sut.GetSummaryAsync(TenantId, segment.Id, null);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Reactivated);
        Assert.Equal(1, result.Retained);
        Assert.Equal(1, result.Dropped);
        Assert.Equal(1, result.NotReturned);
        Assert.Equal(4, result.Reactivated + result.Retained + result.Dropped + result.NotReturned);
        Assert.Equal(4, result.MatchedCount);
        Assert.Equal(2, result.BuyersAfter); // reactivated + retained
        Assert.Equal(2, result.ActiveBefore); // retained + dropped
        Assert.Equal(2, result.InactiveBefore); // reactivated + notReturned
        Assert.NotNull(result.Recommendation);
    }

    // ── Customers table: PII masking + pagination ────────────────────────────────────────────────

    [Fact]
    public async Task GetCustomersAsync_masks_phone_by_default_and_unmasks_only_when_authorized()
    {
        var segment = AnalyzedSegment();
        var customerId = Guid.NewGuid();
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns([customerId]);
        _repo.GetCustomerNamesAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new CustomerNameRow(customerId, "Іван Петренко", "+380671234567")]);
        _repo.GetCustomerPeriodMetricsAsync(TenantId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _marketingAnalytics.GetScoredCustomersAsync(TenantId, null, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (masked, _) = await _sut.GetCustomersAsync(TenantId, segment.Id, null, 1, 20, null, true, canViewUnmaskedPii: false);
        var (unmasked, _) = await _sut.GetCustomersAsync(TenantId, segment.Id, null, 1, 20, null, true, canViewUnmaskedPii: true);

        Assert.NotEqual("+380671234567", masked!.Rows[0].Phone);
        Assert.Equal("+380671234567", unmasked!.Rows[0].Phone);
    }

    [Fact]
    public async Task GetCustomersAsync_returns_zero_rows_without_a_top_200_cap_concept_for_an_empty_segment()
    {
        var segment = AnalyzedSegment();
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns([]);

        var (result, error) = await _sut.GetCustomersAsync(TenantId, segment.Id, null, 1, 20, null, true, false);

        Assert.Null(error);
        Assert.Equal(0, result!.TotalCount);
        Assert.Empty(result.Rows);
    }

    // ── Migration matrix: no-purchase special case vs ordinary low-R classification ─────────────

    [Fact]
    public async Task GetMigrationAsync_marginal_sums_equal_MatchedCount_and_no_purchase_customers_get_the_null_bucket()
    {
        var segment = AnalyzedSegment();

        var neverPurchasedId = Guid.NewGuid();     // absent from every scored call -> "Без покупок"
        var reactivatedWithHistoryId = Guid.NewGuid(); // absent before, present after AND all-time
        var everyWindowId = Guid.NewGuid();        // present in all 3 calls

        var customerIds = new List<Guid> { neverPurchasedId, reactivatedWithHistoryId, everyWindowId };
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(customerIds);

        var champion = new RfmScoredCustomerRow(
            everyWindowId, RecencyScore: 5, FrequencyScore: 5, MonetaryScore: 5,
            PeriodRevenue: 1000m, PeriodReceiptCount: 5,
            FirstPurchaseDateEver: new DateOnly(2025, 1, 1), LifetimeReceiptCount: 50,
            LastPurchaseDateEver: segment.AfterEnd!.Value);

        var afterOnlyRow = new RfmScoredCustomerRow(
            reactivatedWithHistoryId, RecencyScore: 4, FrequencyScore: 2, MonetaryScore: 2,
            PeriodRevenue: 100m, PeriodReceiptCount: 1,
            FirstPurchaseDateEver: new DateOnly(2024, 1, 1), LifetimeReceiptCount: 3,
            LastPurchaseDateEver: segment.AfterEnd.Value);

        // Before window: only everyWindowId purchased.
        _marketingAnalytics.GetScoredCustomersAsync(TenantId, null, segment.BeforeStart!.Value, segment.BeforeEnd!.Value, Arg.Any<CancellationToken>())
            .Returns([champion]);
        // After window: everyWindowId + reactivatedWithHistoryId purchased.
        _marketingAnalytics.GetScoredCustomersAsync(TenantId, null, segment.AfterStart!.Value, segment.AfterEnd.Value, Arg.Any<CancellationToken>())
            .Returns([champion, afterOnlyRow]);
        // All-time: everyWindowId + reactivatedWithHistoryId have real history; neverPurchasedId does not.
        _marketingAnalytics.GetScoredCustomersAsync(TenantId, null, DateOnly.MinValue, segment.AfterEnd.Value, Arg.Any<CancellationToken>())
            .Returns([champion, afterOnlyRow]);

        var (result, error) = await _sut.GetMigrationAsync(TenantId, segment.Id, null);

        Assert.Null(error);
        Assert.NotNull(result);

        // Marginal sums (source doc §25.1/§27.3): rows/columns/matrix cells all sum to MatchedCount.
        Assert.Equal(3, result!.BeforeDistribution.Sum(d => d.Count));
        Assert.Equal(3, result.AfterDistribution.Sum(d => d.Count));
        Assert.Equal(3, result.Matrix.Sum(c => c.Count));
        Assert.Equal(3, result.UpCount + result.StableCount + result.DownCount);

        // neverPurchasedId: absent everywhere -> null "Без покупок" bucket both before and after -> stable.
        // reactivatedWithHistoryId: absent before (real history exists) -> classified via the sentinel
        //   R=F=M=1 "ordinary low-R" branch, which RfmSegmentClassifier resolves to Hibernating (R<=2,
        //   F<=2, M<=2) — a real, non-null segment, never the null "Без покупок" bucket. afterOnlyRow's
        //   actual scores (R4/F2/M2) classify as PotentialLoyalist, not Champions (F/M both fail
        //   Champions' >=4 thresholds) — asserted against that real outcome, not a guessed one.
        var reactivatedCell = result.Matrix.SingleOrDefault(c => c.After == RfmSegmentKey.PotentialLoyalist && c.Before != null);
        Assert.NotNull(reactivatedCell); // "ordinary low-R" branch produced a real (non-null) before-key

        var noPurchaseRow = result.BeforeDistribution.SingleOrDefault(d => d.Segment is null);
        Assert.NotNull(noPurchaseRow);
        Assert.Equal(1, noPurchaseRow!.Count); // exactly neverPurchasedId
    }

    [Fact]
    public async Task GetMigrationAsync_returns_a_valid_zeroed_result_for_an_empty_segment()
    {
        var segment = AnalyzedSegment();
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns([]);

        var (result, error) = await _sut.GetMigrationAsync(TenantId, segment.Id, null);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Empty(result!.Matrix);
        Assert.Equal(0, result.UpCount + result.StableCount + result.DownCount);
        await _marketingAnalytics.DidNotReceive().GetScoredCustomersAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ── Exports ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportUnknownTokensAsync_builds_rows_from_both_sample_lists_and_logs_activity()
    {
        var segment = new PostCampaignSegment
        {
            TenantId = TenantId,
            CreatedByUserId = UserId,
            UnknownTokensSample = ["unknown-1"],
            UnknownCount = 1,
            InvalidTokensSample = ["bad-token"],
            InvalidCount = 1,
        };
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);

        var (result, error) = await _sut.ExportUnknownTokensAsync(TenantId, UserId, segment.Id);

        Assert.Null(error);
        Assert.NotNull(result);
        // Sample holds every token (1 of 1 each) -> not truncated, no extra note row.
        Assert.Equal(1, result!.TotalUnknownCount);
        Assert.Equal(1, result.TotalInvalidCount);
        Assert.False(result.Truncated);
        _excelExport.Received(1).Export(Arg.Is<ExcelExportRequest>(r => r.Rows.Count == 2));
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a => a.Action == "marketing_analytics.post_campaign.export_unknown_tokens"), Arg.Any<CancellationToken>());
    }

    // ── Exports: TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20) ────

    [Fact]
    public async Task ExportUnknownTokensAsync_reports_truncation_and_inserts_a_note_row_when_the_real_count_exceeds_the_sample()
    {
        var segment = new PostCampaignSegment
        {
            TenantId = TenantId,
            CreatedByUserId = UserId,
            UnknownTokensSample = ["unknown-1", "unknown-2"],
            UnknownCount = 25, // real count exceeds what the (capped) sample holds
            InvalidTokensSample = ["bad-token"],
            InvalidCount = 1, // NOT truncated on this side
        };
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);

        var (result, error) = await _sut.ExportUnknownTokensAsync(TenantId, UserId, segment.Id);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(25, result!.TotalUnknownCount);
        Assert.Equal(1, result.TotalInvalidCount);
        Assert.True(result.Truncated); // true even though only the UNKNOWN side is short

        // 1 note row (inserted first) + 2 unknown sample rows + 1 invalid sample row = 4.
        _excelExport.Received(1).Export(Arg.Is<ExcelExportRequest>(r =>
            r.Rows.Count == 4 &&
            (string)r.Rows[0][1]! == "Примітка" &&
            ((string)r.Rows[0][0]!).Contains("25")));
    }

    [Fact]
    public async Task ExportUnknownTokensAsync_returns_not_found_for_a_missing_segment()
    {
        _repo.GetSegmentAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PostCampaignSegment?)null);

        var (result, error) = await _sut.ExportUnknownTokensAsync(TenantId, UserId, Guid.NewGuid());

        Assert.Null(result);
        Assert.EndsWith("not found.", error);
    }

    // ── Explain (AI) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainAsync_feeds_the_summarys_own_kpis_into_the_advisor_context()
    {
        var segment = AnalyzedSegment();
        segment.Name = "SMS блиц";
        _repo.GetSegmentAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns(segment);
        _repo.GetMemberCustomerIdsAsync(TenantId, segment.Id, Arg.Any<CancellationToken>()).Returns([]);
        _advisor.ExplainAsync(Arg.Any<PostCampaignAdvisorContext>(), Arg.Any<CancellationToken>())
            .Returns(new PostCampaignAdvisorResult("пояснення", "claude-sonnet-4-6", 123));

        var (result, error) = await _sut.ExplainAsync(TenantId, segment.Id, null);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("пояснення", result!.ExplanationUa);
        await _advisor.Received(1).ExplainAsync(
            Arg.Is<PostCampaignAdvisorContext>(c => c.TitleUa.Contains("SMS блиц")), Arg.Any<CancellationToken>());
    }
}
