using NSubstitute;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.AudienceBuilder;

/// <summary>
/// TASK-429: AudienceBuilderService orchestration — Terms-list splitting into the
/// ResolvedAudienceQuery/ResolvedCompetitorQuery shape the repository consumes (term_index
/// preservation across interleaved text/category kinds, malformed-term dropping, AND/OR ->
/// MatchAll mapping), the "no valid terms -> never touch the database, return a zeroed DTO"
/// short-circuit, and the PII-masking/ActivityLog export contract applied from day 0 (design doc
/// §9) rather than patched in later. SQL correctness (the UNNEST term matching itself, the
/// competitor MINUS set difference, the double-counting fix for overlapping terms) lives in
/// AudienceBuilderRepositoryIntegrationTests (live Postgres) — this file mocks the repository's
/// already-computed raw rows.
/// </summary>
public sealed class AudienceBuilderServiceTests
{
    private readonly IAudienceBuilderRepository _repo = Substitute.For<IAudienceBuilderRepository>();
    private readonly IExcelExportService _excel = Substitute.For<IExcelExportService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly AudienceBuilderService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    public AudienceBuilderServiceTests()
    {
        _sut = new AudienceBuilderService(_repo, _excel, _activityLogs);
        _excel.Export(Arg.Any<ExcelExportRequest>())
            .Returns(ci => new ExcelExportResult([], ci.Arg<ExcelExportRequest>().Rows.Count, false));
    }

    private static AudienceBuildRequest BuildRequest(
        IReadOnlyList<AudienceTermRequest> terms, AudienceCombineMode mode = AudienceCombineMode.Any,
        decimal? minQty = null, decimal? minAmount = null, IReadOnlyList<Guid>? excluded = null,
        int page = 1, int pageSize = 20, bool canViewUnmaskedPii = false) =>
        new(From, To, null, terms, mode, minQty, minAmount, excluded, page, pageSize, null, true, canViewUnmaskedPii);

    // ── Empty / no-valid-terms short-circuit ─────────────────────────────────────────────────

    [Fact]
    public async Task GetOverviewAsync_with_no_terms_never_touches_the_repository_and_returns_zeroed_dto()
    {
        var result = await _sut.GetOverviewAsync(TenantId, BuildRequest([]));

        Assert.Equal(0, result.ParticipantsCount);
        Assert.Equal(0, result.ItemsInSelectionCount);
        Assert.Equal(0m, result.UnitsPurchased);
        Assert.Equal(0m, result.TotalSpend);
        await _repo.DidNotReceive().GetOverviewAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBuyersAsync_with_only_malformed_terms_never_touches_the_repository()
    {
        // Category term with no CategoryId, Text term with blank text — both silently dropped.
        var terms = new[]
        {
            new AudienceTermRequest(AudienceTermKind.Category, null, null),
            new AudienceTermRequest(AudienceTermKind.Text, "   ", null),
        };

        var result = await _sut.GetBuyersAsync(TenantId, BuildRequest(terms));

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Rows);
        await _repo.DidNotReceive().GetBuyersAsync(
            Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCompetitorOverviewAsync_requires_both_own_and_competitor_terms()
    {
        var ownOnly = new CompetitorAudienceRequest(
            From, To, null,
            [new AudienceTermRequest(AudienceTermKind.Text, "кока кола", null)], null,
            [], CompetitorHorizon.InPeriod);

        var result = await _sut.GetCompetitorOverviewAsync(TenantId, ownOnly);

        Assert.Equal(0, result.NewAudienceCount);
        await _repo.DidNotReceive().GetCompetitorOverviewAsync(Arg.Any<ResolvedCompetitorQuery>(), Arg.Any<CancellationToken>());
    }

    // ── Term splitting: index preservation, kind interleaving, AND/OR mapping ────────────────

    [Fact]
    public async Task GetOverviewAsync_splits_interleaved_text_and_category_terms_preserving_original_index()
    {
        var categoryId = Guid.NewGuid();
        var terms = new[]
        {
            new AudienceTermRequest(AudienceTermKind.Text, "кока кола", null),   // index 0
            new AudienceTermRequest(AudienceTermKind.Category, null, categoryId), // index 1
            new AudienceTermRequest(AudienceTermKind.Text, "фанта", null),       // index 2
        };

        _repo.GetOverviewAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AudienceOverviewRow(0, 0, 0m, 0m));

        await _sut.GetOverviewAsync(TenantId, BuildRequest(terms, AudienceCombineMode.All));

        await _repo.Received(1).GetOverviewAsync(
            Arg.Is<ResolvedAudienceQuery>(q =>
                q.TenantId == TenantId &&
                q.MatchAll &&
                q.TermCount == 3 &&
                q.TextTermIndexes.SequenceEqual(new[] { 0, 2 }) &&
                q.TextTermValues.SequenceEqual(new[] { "кока кола", "фанта" }) &&
                q.CategoryTermIndexes.SequenceEqual(new[] { 1 }) &&
                q.CategoryTermIds.SequenceEqual(new[] { categoryId })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOverviewAsync_any_mode_maps_to_MatchAll_false()
    {
        _repo.GetOverviewAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AudienceOverviewRow(0, 0, 0m, 0m));

        await _sut.GetOverviewAsync(TenantId, BuildRequest(
            [new AudienceTermRequest(AudienceTermKind.Text, "кола", null)], AudienceCombineMode.Any));

        await _repo.Received(1).GetOverviewAsync(Arg.Is<ResolvedAudienceQuery>(q => q.MatchAll == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOverviewAsync_trims_text_terms_and_normalizes_excluded_and_store_ids()
    {
        var dupStore = Guid.NewGuid();
        var dupExcluded = Guid.NewGuid();

        _repo.GetOverviewAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AudienceOverviewRow(0, 0, 0m, 0m));

        var request = BuildRequest(
            [new AudienceTermRequest(AudienceTermKind.Text, "  кола  ", null)],
            excluded: [dupExcluded, dupExcluded]) with
        { StoreIds = [dupStore, dupStore] };

        await _sut.GetOverviewAsync(TenantId, request);

        await _repo.Received(1).GetOverviewAsync(
            Arg.Is<ResolvedAudienceQuery>(q =>
                q.TextTermValues.Single() == "кола" &&
                q.StoreIds.Single() == dupStore &&
                q.ExcludedItemIds.Single() == dupExcluded),
            Arg.Any<CancellationToken>());
    }

    // ── Paging ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBuyersAsync_normalizes_out_of_range_page_and_page_size()
    {
        _repo.GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AudienceBuyerRowRaw>());

        var request = BuildRequest([new AudienceTermRequest(AudienceTermKind.Text, "кола", null)], page: 0, pageSize: 9999);

        var result = await _sut.GetBuyersAsync(TenantId, request);

        Assert.Equal(1, result.Page);       // clamped up from 0
        Assert.Equal(20, result.PageSize);  // out-of-range falls back to default (20), not clamped to max
        await _repo.Received(1).GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), 1, 20, Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ── PII masking (design doc §9 — applied from day 0) ────────────────────────────────────

    [Fact]
    public async Task GetBuyersAsync_masks_phone_by_default_and_unmasks_when_authorized()
    {
        var customerId = Guid.NewGuid();
        _repo.GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AudienceBuyerRowRaw>
            {
                new(customerId, "Іван Петренко", "+380671234567", 5m, 2, 500m, 1, 1),
            });

        var terms = new[] { new AudienceTermRequest(AudienceTermKind.Text, "кола", null) };

        var masked = await _sut.GetBuyersAsync(TenantId, BuildRequest(terms, canViewUnmaskedPii: false));
        Assert.Equal("+380 67 *** ** 67", masked.Rows.Single().Phone);

        var unmasked = await _sut.GetBuyersAsync(TenantId, BuildRequest(terms, canViewUnmaskedPii: true));
        Assert.Equal("+380671234567", unmasked.Rows.Single().Phone);
    }

    [Fact]
    public async Task GetCompetitorBuyersAsync_masks_phone_by_default()
    {
        var customerId = Guid.NewGuid();
        _repo.GetCompetitorBuyersAsync(Arg.Any<ResolvedCompetitorQuery>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompetitorBuyerRowRaw>
            {
                new(customerId, "Марія Коваль", "+380991234567", 3m, 1, 300m, 1, 1),
            });

        var request = new CompetitorAudienceRequest(
            From, To, null,
            [new AudienceTermRequest(AudienceTermKind.Text, "кока кола", null)], null,
            [new AudienceTermRequest(AudienceTermKind.Text, "фанта", null)], CompetitorHorizon.InPeriod);

        var result = await _sut.GetCompetitorBuyersAsync(TenantId, request);

        Assert.Equal("+380 99 *** ** 67", result.Rows.Single().Phone);
    }

    // ── Exports: ActivityLog + repository call shape ────────────────────────────────────────

    [Fact]
    public async Task ResolveCustomerIdsAsync_reads_every_page_without_the_ui_page_size_cap()
    {
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();
        _repo.GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), 200, "name", false, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var page = call.ArgAt<int>(1);
                return ids.Skip((page - 1) * 200).Take(200)
                    .Select(id => new AudienceBuyerRowRaw(id, "Покупець", null, 1, 1, 10, 201, 0))
                    .ToArray();
            });

        var result = await _sut.ResolveCustomerIdsAsync(TenantId, BuildRequest(
            [new AudienceTermRequest(AudienceTermKind.Text, "кава", null)]));

        Assert.Equal(201, result.Count);
        await _repo.Received(1).GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), 1, 200, "name", false, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetBuyersAsync(Arg.Any<ResolvedAudienceQuery>(), 2, 200, "name", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBuyersAsync_calls_receipt_level_repository_method_and_logs_activity()
    {
        _repo.GetBuyerReceiptsAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<AudienceReceiptExportRowRaw>
            {
                new(Guid.NewGuid(), "Іван", "+380671234567", "R-001", DateTime.UtcNow, "Магазин 1", 2m, 200m),
            });

        var request = new ExportAudienceBuyersRequest(
            From, To, null, [new AudienceTermRequest(AudienceTermKind.Text, "кола", null)],
            AudienceCombineMode.Any, null, null, null, UnmaskPii: false);

        var result = await _sut.ExportBuyersAsync(TenantId, UserId, request);

        Assert.Equal(1, result.RowCount);
        await _repo.Received(1).GetBuyerReceiptsAsync(Arg.Any<ResolvedAudienceQuery>(), 50_000, Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l =>
                l.TenantId == TenantId && l.UserId == UserId &&
                l.Action == "marketing_analytics.audience_builder.export_buyers" &&
                l.Meta != null && l.Meta.Contains("piiMasked=True")),
            Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportCompetitorBuyersAsync_reuses_paginated_repository_method_at_export_page_size_and_logs_activity()
    {
        _repo.GetCompetitorBuyersAsync(Arg.Any<ResolvedCompetitorQuery>(), 1, 50_000, null, true, Arg.Any<CancellationToken>())
            .Returns(new List<CompetitorBuyerRowRaw>());

        var request = new ExportCompetitorBuyersRequest(
            From, To, null,
            [new AudienceTermRequest(AudienceTermKind.Text, "кока кола", null)], null,
            [new AudienceTermRequest(AudienceTermKind.Text, "фанта", null)], CompetitorHorizon.AllTime, UnmaskPii: true);

        await _sut.ExportCompetitorBuyersAsync(TenantId, UserId, request);

        await _repo.Received(1).GetCompetitorBuyersAsync(Arg.Any<ResolvedCompetitorQuery>(), 1, 50_000, null, true, Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "marketing_analytics.audience_builder.export_competitor_buyers"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBuyersAsync_with_no_valid_terms_skips_repository_but_still_produces_an_empty_export()
    {
        var request = new ExportAudienceBuyersRequest(
            From, To, null, [], AudienceCombineMode.Any, null, null, null, UnmaskPii: false);

        var result = await _sut.ExportBuyersAsync(TenantId, UserId, request);

        Assert.Equal(0, result.RowCount);
        await _repo.DidNotReceive().GetBuyerReceiptsAsync(Arg.Any<ResolvedAudienceQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).LogAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    // ── Categories passthrough ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchCategoriesAsync_maps_rows_and_normalizes_out_of_range_limit()
    {
        var categoryId = Guid.NewGuid();
        _repo.SearchCategoriesAsync(TenantId, "напої", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<AudienceCategoryOptionRow> { new(categoryId, "Напої", 34) });

        var result = await _sut.SearchCategoriesAsync(TenantId, "напої", limit: 0);

        Assert.Single(result);
        Assert.Equal(categoryId, result[0].CategoryId);
        Assert.Equal("Напої", result[0].Name);
        Assert.Equal(34, result[0].ItemCount);
        await _repo.Received(1).SearchCategoriesAsync(TenantId, "напої", 20, Arg.Any<CancellationToken>());
    }
}
