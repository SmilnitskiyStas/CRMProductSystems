using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-420: live-Postgres coverage for <see cref="PriceSegmentsRepository"/> — the second raw-
/// SQL repository in this codebase, and the first one anywhere in it to pass a genuinely
/// NULLABLE scalar SQL parameter (optional segment/spend filters). A mocked/NSubstitute unit
/// test structurally cannot verify any of this (PERCENTILE_CONT correctness, the dynamic-
/// ORDER-BY string.Replace mechanism, the null-vs-DBNull.Value parameter path, or that the SQL
/// CASE ladders genuinely agree with the pure classifiers) — same rationale
/// MarketingAnalyticsRepositoryIntegrationTests already established for TASK-406. Same
/// connection/skip pattern: real `crm` superuser connection, no RLS session vars (SQL
/// correctness under test, not tenant isolation).
///
/// Fixture shape (see InitializeAsync): a spread of "filler" customers (all-time-only, dated
/// far in the past) gives GetBoundariesAsync/GetAllTimeDistributionAsync a non-trivial P20-P97
/// spread; 4 "comparison" customers (RealGrowth/PriceGrowth/Declining ×2/Stable) each get
/// exactly one receipt per window so their own MedianCheck is unambiguous; 3 "frequency"
/// customers (Sleeping/Growing/Declining) each have receipts in only one window or a controlled
/// receipt-count delta; 1 dedicated customer isolates the network unit-price weighted average.
/// Every cross-check below classifies the repository's raw ordinals through the SAME pure
/// classifier (PriceSegmentClassifier/PriceAudienceClassifier/FrequencyAudienceClassifier) the
/// SQL is supposed to mirror — directly verifying the "kept in sync" promise in both files'
/// doc comments, rather than hand-predicting percentile arithmetic.
/// </summary>
public sealed class PriceSegmentsRepositoryIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;
    private DbContextOptions<AppDbContext>? _options;

    private Guid _tenantId;
    private Guid _locationId;
    private Guid _bananaItemId;
    private Guid _packagingItemId;

    private Guid _realGrowth, _priceGrowth, _declining, _declining2, _stable;
    private Guid _freqSleeping, _freqGrowing, _freqDeclining;
    private Guid _avgPriceCustomer;
    private readonly List<Guid> _fillerCustomers = [];

    private DateOnly _currentFrom, _currentTo, _previousFrom, _previousTo;
    private DateTime _currentReceiptAt, _previousReceiptAt, _fillerReceiptAt, _avgPriceReceiptAt;

    public PriceSegmentsRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping PriceSegmentsRepository integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentFrom = today.AddDays(-29);
        _currentTo = today;
        _previousTo = _currentFrom.AddDays(-1);
        _previousFrom = _previousTo.AddDays(-29);
        _currentReceiptAt = _currentTo.AddDays(-5).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        _previousReceiptAt = _previousTo.AddDays(-5).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        _fillerReceiptAt = DateTime.UtcNow.AddDays(-500);
        // Deliberately a DIFFERENT calendar day than _currentReceiptAt (still inside the current
        // 30-day window) so GetAverageUnitPriceAsync can isolate exactly these 2 receipts with a
        // tight day-window query, uncontaminated by every other fixture customer's own
        // current-window banana purchases (which all share _currentReceiptAt's own day).
        _avgPriceReceiptAt = _currentTo.AddDays(-10).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        await using var db = NewContext();

        var tenant = Tenant.Create($"PriceSegments Repo Test {Guid.NewGuid():N}", $"price-segments-repo-test-{Guid.NewGuid():N}");
        _tenantId = tenant.Id;

        var location = new Location { TenantId = _tenantId, Name = "Test Store" };
        _locationId = location.Id;

        var banana = new Item { TenantId = _tenantId, Name = "Банан", ItemType = "product" };
        var packaging = new Item { TenantId = _tenantId, Name = "Коробка", ItemType = "packaging" };
        _bananaItemId = banana.Id;
        _packagingItemId = packaging.Id;

        db.Tenants.Add(tenant);
        db.Locations.Add(location);
        db.Items.AddRange(banana, packaging);

        Guid NewCustomer(string name, string? phone = null)
        {
            var c = new Customer { TenantId = _tenantId, Name = name, Phone = phone };
            db.Customers.Add(c);
            return c.Id;
        }

        // Fillers: single all-time receipt each, wide spread (50..900), dated far in the past —
        // gives GetBoundariesAsync/GetAllTimeDistributionAsync a real, non-trivial P20-P97 spread
        // without any of them ever landing inside the current/previous comparison windows.
        foreach (var value in new decimal[] { 50m, 120m, 250m, 400m, 650m, 900m })
            _fillerCustomers.Add(NewCustomer($"Filler {value}"));

        _realGrowth = NewCustomer("RealGrowth", "+380671110001");
        _priceGrowth = NewCustomer("PriceGrowth");
        _declining = NewCustomer("Declining");
        _declining2 = NewCustomer("Declining2");
        _stable = NewCustomer("Stable");
        _freqSleeping = NewCustomer("FreqSleeping");
        _freqGrowing = NewCustomer("FreqGrowing");
        _freqDeclining = NewCustomer("FreqDeclining");
        _avgPriceCustomer = NewCustomer("AvgPriceCustomer");

        await db.SaveChangesAsync();

        var transactions = new List<PosTransaction>();
        var items = new List<PosTransactionItem>();
        var receiptSeq = 0;
        string NextReceipt() => $"PS-{++receiptSeq:0000}";

        Guid AddReceipt(Guid customerId, DateTime createdAtUtc, decimal totalAmount, IReadOnlyList<(Guid ProductId, decimal Quantity, decimal PriceFinal)> lines)
        {
            var tx = new PosTransaction
            {
                TenantId = _tenantId,
                StoreId = _locationId,
                CustomerId = customerId,
                ReceiptNumber = NextReceipt(),
                TotalAmount = totalAmount,
                CreatedAt = createdAtUtc,
            };
            transactions.Add(tx);
            foreach (var (productId, qty, priceFinal) in lines)
            {
                items.Add(new PosTransactionItem
                {
                    TransactionId = tx.Id,
                    ProductId = productId,
                    Quantity = qty,
                    PriceRetail = priceFinal,
                    DiscountAmount = 0,
                    PriceFinal = priceFinal,
                });
            }
            return tx.Id;
        }

        // Fillers: 1 old receipt each, exactly the target amount, 1 banana unit (irrelevant to
        // any assertion, just keeps the fixture internally consistent).
        var fillerValues = new decimal[] { 50m, 120m, 250m, 400m, 650m, 900m };
        for (var i = 0; i < _fillerCustomers.Count; i++)
            AddReceipt(_fillerCustomers[i], _fillerReceiptAt, fillerValues[i], [(_bananaItemId, 1m, fillerValues[i])]);

        // RealGrowth: previous LOW check + few items, current HIGH check + MORE items -> segment
        // up AND items/receipt up -> RealGrowth.
        AddReceipt(_realGrowth, _previousReceiptAt, 300m, [(_bananaItemId, 2m, 150m)]);
        AddReceipt(_realGrowth, _currentReceiptAt, 900m, [(_bananaItemId, 5m, 180m), (_packagingItemId, 2m, 10m)]);

        // PriceGrowth: previous LOW, current HIGH, items/receipt UNCHANGED (3 -> 3) -> "рівність
        // теж через ціни" (design doc §1.6).
        AddReceipt(_priceGrowth, _previousReceiptAt, 300m, [(_bananaItemId, 3m, 100m)]);
        AddReceipt(_priceGrowth, _currentReceiptAt, 900m, [(_bananaItemId, 3m, 300m)]);

        // Declining (×2, different TypicalCheckCurrent, for sort/pagination coverage): previous
        // HIGH, current LOW -> segment down regardless of items -> Declining.
        AddReceipt(_declining, _previousReceiptAt, 900m, [(_bananaItemId, 3m, 300m)]);
        AddReceipt(_declining, _currentReceiptAt, 100m, [(_bananaItemId, 3m, 33.33m)]);
        AddReceipt(_declining2, _previousReceiptAt, 650m, [(_bananaItemId, 2m, 325m)]);
        AddReceipt(_declining2, _currentReceiptAt, 60m, [(_bananaItemId, 2m, 30m)]);

        // Stable: IDENTICAL median check both windows -> same tier regardless of exact
        // boundaries -> Stable, deterministically.
        AddReceipt(_stable, _previousReceiptAt, 300m, [(_bananaItemId, 3m, 100m)]);
        AddReceipt(_stable, _currentReceiptAt, 300m, [(_bananaItemId, 3m, 100m)]);

        // Frequency: Sleeping (previous>0, current=0), Growing (previous=0, current>0),
        // Declining (previous=4, current=1 -> 75% decline >= 30% default threshold).
        for (var i = 0; i < 3; i++)
            AddReceipt(_freqSleeping, _previousReceiptAt.AddHours(i), 100m, [(_bananaItemId, 1m, 100m)]);
        for (var i = 0; i < 4; i++)
            AddReceipt(_freqGrowing, _currentReceiptAt.AddHours(i), 100m, [(_bananaItemId, 1m, 100m)]);
        for (var i = 0; i < 4; i++)
            AddReceipt(_freqDeclining, _previousReceiptAt.AddHours(i), 100m, [(_bananaItemId, 1m, 100m)]);
        AddReceipt(_freqDeclining, _currentReceiptAt, 100m, [(_bananaItemId, 1m, 100m)]);

        // Dedicated network-unit-price fixture: weighted avg = (2*100 + 3*120) / (2+3) = 112,
        // packaging (qty=1, price=50) must be excluded entirely from both sums. Own calendar
        // day (_avgPriceReceiptAt), so a tight window isolates exactly these 2 receipts.
        AddReceipt(_avgPriceCustomer, _avgPriceReceiptAt, 200m, [(_bananaItemId, 2m, 100m)]);
        AddReceipt(_avgPriceCustomer, _avgPriceReceiptAt.AddHours(1), 410m, [(_bananaItemId, 3m, 120m), (_packagingItemId, 1m, 50m)]);

        db.PosTransactions.AddRange(transactions);
        db.PosTransactionItems.AddRange(items);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transaction_items WHERE \"TransactionId\" IN (SELECT \"Id\" FROM pos_transactions WHERE \"TenantId\" = {_tenantId})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transactions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM price_segment_settings WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    // ── Per-customer metrics + packaging exclusion ──────────────────────────────────────────

    [Fact]
    public async Task GetCustomerPeriodMetricsAsync_computes_median_spend_and_excludes_packaging_from_items_per_receipt()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        var currentRows = await repo.GetCustomerPeriodMetricsAsync(_tenantId, null, _currentFrom, _currentTo);

        var realGrowth = currentRows.Single(r => r.CustomerId == _realGrowth);
        Assert.Equal(900m, realGrowth.MedianCheck);
        Assert.Equal(1, realGrowth.ReceiptCount);
        Assert.Equal(900m, realGrowth.TotalSpend);
        // 5 banana + 2 packaging on the one receipt -> packaging excluded -> 5/1 = 5.
        Assert.Equal(5m, realGrowth.ItemsPerReceipt);

        // Fillers are 500 days old — must be entirely absent from the 30-day current window.
        Assert.DoesNotContain(currentRows, r => _fillerCustomers.Contains(r.CustomerId));
    }

    // ── Boundaries + network unit price ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoundariesAsync_returns_ascending_non_trivial_boundaries()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        var b = await repo.GetBoundariesAsync(_tenantId, null);

        Assert.True(b.P20 <= b.P40);
        Assert.True(b.P40 <= b.P60);
        Assert.True(b.P60 <= b.P80);
        Assert.True(b.P80 <= b.P90);
        Assert.True(b.P90 <= b.P97);
        Assert.True(b.P97 > 0); // non-trivial given the 50..900 filler spread
    }

    [Fact]
    public async Task GetAverageUnitPriceAsync_weights_by_quantity_and_excludes_packaging()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        // _avgPriceReceiptAt is a calendar day no other fixture customer transacts on, so this
        // day-window isolates exactly _avgPriceCustomer's 2 receipts: (2*100 + 3*120)/(2+3) =
        // 112, with the packaging line (qty=1, price=50) excluded from both sums entirely — if
        // it leaked in, the result would be pulled toward (2*100+3*120+1*50)/6 = 95, not 112.
        var windowFrom = DateOnly.FromDateTime(_avgPriceReceiptAt);
        var windowTo = DateOnly.FromDateTime(_avgPriceReceiptAt);
        var avg = await repo.GetAverageUnitPriceAsync(_tenantId, null, windowFrom, windowTo);

        Assert.Equal(112m, avg);
    }

    [Fact]
    public async Task GetLtvMapAsync_is_all_time_and_empty_customer_list_means_everyone()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        var single = await repo.GetLtvMapAsync(_tenantId, [_realGrowth], null);
        Assert.Equal(1200m, single.Single(r => r.CustomerId == _realGrowth).Ltv); // 300 + 900, all-time

        var everyone = await repo.GetLtvMapAsync(_tenantId, null, null);
        Assert.Contains(everyone, r => r.CustomerId == _realGrowth && r.Ltv == 1200m);
        Assert.Contains(everyone, r => _fillerCustomers.Contains(r.CustomerId));
    }

    // ── All-time KPIs / trend / distribution ────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTimeKpiAsync_and_GetAllTimeDistributionAsync_cover_every_customer_exactly_once()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        // 6 fillers + realGrowth/priceGrowth/declining/declining2/stable (5) + 3 frequency
        // customers + 1 avg-price customer = 15 distinct customers with purchase history.
        const int expectedCustomers = 15;

        var kpi = await repo.GetAllTimeKpiAsync(_tenantId, null);
        Assert.Equal(expectedCustomers, kpi.CustomersInBase);
        Assert.True(kpi.PurchasesTotal >= expectedCustomers);
        Assert.True(kpi.TurnoverTotal > 0);

        var boundaries = await repo.GetBoundariesAsync(_tenantId, null);
        var distribution = await repo.GetAllTimeDistributionAsync(_tenantId, null, boundaries);
        Assert.Equal(expectedCustomers, distribution.Sum(d => d.CustomerCount));
        Assert.All(distribution, d => Assert.InRange(d.SegmentOrdinal, 1, 7));
    }

    [Fact]
    public async Task GetMonthlyTrendAsync_returns_months_in_ascending_order()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        var trend = await repo.GetMonthlyTrendAsync(_tenantId, null);

        Assert.NotEmpty(trend);
        for (var i = 1; i < trend.Count; i++)
        {
            var prevKey = trend[i - 1].Year * 12 + trend[i - 1].Month;
            var curKey = trend[i].Year * 12 + trend[i].Month;
            Assert.True(curKey > prevKey, "Months must be strictly ascending with no duplicates.");
        }
        // The 500-day-old filler receipts must land in an earlier month than the current-window ones.
        var fillerKey = trend[0].Year * 12 + trend[0].Month;
        var lastKey = trend[^1].Year * 12 + trend[^1].Month;
        Assert.True(fillerKey < lastKey);
    }

    // ── Comparison-mode paginated audience table ─────────────────────────────────────────────

    [Fact]
    public async Task GetPriceAudienceTableAsync_filters_each_audience_correctly_and_agrees_with_the_pure_classifier()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);
        var boundaries = await repo.GetBoundariesAsync(_tenantId, null);

        async Task<IReadOnlyList<PriceAudienceTableRowRaw>> Fetch(PriceAudienceKey audience, int page = 1, int pageSize = 50, string? sortBy = null, bool desc = false) =>
            await repo.GetPriceAudienceTableAsync(
                _tenantId, null, _currentFrom, _currentTo, _previousFrom, _previousTo, boundaries, (int)audience, page, pageSize, sortBy, desc);

        var realGrowthRows = await Fetch(PriceAudienceKey.RealGrowth);
        var realGrowthRow = Assert.Single(realGrowthRows);
        Assert.Equal(_realGrowth, realGrowthRow.CustomerId);
        Assert.Equal(
            PriceAudienceKey.RealGrowth,
            PriceAudienceClassifier.Classify(
                PriceSegmentCatalog.FromOrdinal(realGrowthRow.PreviousSegmentOrdinal),
                PriceSegmentCatalog.FromOrdinal(realGrowthRow.CurrentSegmentOrdinal),
                realGrowthRow.ItemsPerReceiptPrevious, realGrowthRow.ItemsPerReceiptCurrent));
        // "Проаналізовано" (comparison cohort) = every customer with receipts in BOTH windows:
        // RealGrowth, PriceGrowth, Declining, Declining2, Stable, AND FreqDeclining (its previous
        // window has 4 receipts, one of which — deliberately, for the frequency fixture — lands
        // inside the SAME 30-day previous window as the price-comparison test, so it legitimately
        // qualifies for the price-comparison cohort too, landing in "Stable" since its previous
        // and current median check are both 100).
        Assert.Equal(6, realGrowthRow.AnalyzedCount);
        Assert.Equal("+380671110001", realGrowthRow.Phone);

        var priceGrowthRows = await Fetch(PriceAudienceKey.PriceGrowth);
        var priceGrowthRow = Assert.Single(priceGrowthRows);
        Assert.Equal(_priceGrowth, priceGrowthRow.CustomerId);
        Assert.Equal(
            PriceAudienceKey.PriceGrowth,
            PriceAudienceClassifier.Classify(
                PriceSegmentCatalog.FromOrdinal(priceGrowthRow.PreviousSegmentOrdinal),
                PriceSegmentCatalog.FromOrdinal(priceGrowthRow.CurrentSegmentOrdinal),
                priceGrowthRow.ItemsPerReceiptPrevious, priceGrowthRow.ItemsPerReceiptCurrent));

        var stableRows = await Fetch(PriceAudienceKey.Stable);
        Assert.Contains(stableRows, r => r.CustomerId == _stable);

        // Declining has 2 members — exercises real pagination + sort direction. Neither
        // Declining nor Declining2 was given a Phone (only RealGrowth was), so WithPhoneCount
        // must be exactly 0 here — a real assertion, not a tautology.
        var decliningAll = await Fetch(PriceAudienceKey.Declining, sortBy: "check", desc: true);
        Assert.Equal(2, decliningAll.Count);
        Assert.Equal(0, decliningAll[0].WithPhoneCount);
        Assert.True(decliningAll[0].TypicalCheckCurrent >= decliningAll[1].TypicalCheckCurrent);

        var decliningPage1 = await Fetch(PriceAudienceKey.Declining, page: 1, pageSize: 1, sortBy: "check", desc: true);
        var decliningPage2 = await Fetch(PriceAudienceKey.Declining, page: 2, pageSize: 1, sortBy: "check", desc: true);
        Assert.Single(decliningPage1);
        Assert.Single(decliningPage2);
        Assert.NotEqual(decliningPage1[0].CustomerId, decliningPage2[0].CustomerId);
        Assert.Equal(2, decliningPage1[0].TotalCount); // TotalCount reflects the WHOLE filtered set, not just this page
    }

    // ── All-time paginated customer table (nullable segmentOrdinal — the null-vs-DBNull path) ─

    [Fact]
    public async Task GetAllTimeCustomerTableAsync_null_segment_returns_everyone_specific_segment_filters()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);
        var boundaries = await repo.GetBoundariesAsync(_tenantId, null);

        var allRows = await repo.GetAllTimeCustomerTableAsync(_tenantId, null, boundaries, segmentOrdinal: null, page: 1, pageSize: 100, sortBy: null, sortDescending: false);
        Assert.Equal(15, allRows.Count);
        Assert.All(allRows, r => Assert.Equal(15, r.TotalCount));

        // Every row's own segment_ordinal must agree with what the pure classifier derives from
        // its own TypicalCheck against the SAME boundaries.
        foreach (var row in allRows)
        {
            var expected = PriceSegmentClassifier.Classify(row.TypicalCheck, boundaries);
            Assert.Equal((int)expected, row.SegmentOrdinal);
        }

        var oneOrdinal = allRows[0].SegmentOrdinal;
        var filtered = await repo.GetAllTimeCustomerTableAsync(_tenantId, null, boundaries, segmentOrdinal: oneOrdinal, page: 1, pageSize: 100, sortBy: null, sortDescending: false);
        Assert.NotEmpty(filtered);
        Assert.All(filtered, r => Assert.Equal(oneOrdinal, r.SegmentOrdinal));
        Assert.True(filtered.Count <= allRows.Count);
        Assert.Equal(filtered.Count, filtered[0].TotalCount); // this filtered set's own total, not the unfiltered 15
    }

    // ── Frequency paginated table (nullable minSpend/maxSpend/priceSegmentOrdinal, useCurrentBasis) ─

    [Fact]
    public async Task GetFrequencyAudienceTableAsync_classifies_each_audience_and_reorients_sleeping_to_previous_basis()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);
        var boundaries = await repo.GetBoundariesAsync(_tenantId, null);
        const decimal declineThreshold = 30m;

        async Task<IReadOnlyList<FrequencyAudienceTableRowRaw>> Fetch(
            FrequencyAudienceKey audience, bool useCurrentBasis, decimal? minSpend = null, decimal? maxSpend = null, int? priceSegment = null) =>
            await repo.GetFrequencyAudienceTableAsync(
                _tenantId, null, _currentFrom, _currentTo, _previousFrom, _previousTo, declineThreshold, boundaries,
                (int)audience, useCurrentBasis, minSpend, maxSpend, priceSegment, page: 1, pageSize: 50, sortBy: null, sortDescending: false);

        // Sleeping: current_frequency=0 by construction -> TypicalCheckCurrent must be NULL
        // (design doc §4 — never a misleading 0), and useCurrentBasis=false re-orients the
        // (absent, in this call) spend/segment filters to the PREVIOUS basis.
        var sleepingRows = await Fetch(FrequencyAudienceKey.Sleeping, useCurrentBasis: false);
        var sleepingRow = Assert.Single(sleepingRows, r => r.CustomerId == _freqSleeping);
        Assert.Equal(0, sleepingRow.CurrentFrequency);
        Assert.Equal(3, sleepingRow.PreviousFrequency);
        Assert.Null(sleepingRow.TypicalCheckCurrent);
        Assert.Equal(-100m, sleepingRow.FrequencyDeltaPercent); // (0-3)/3*100

        var growingRows = await Fetch(FrequencyAudienceKey.Growing, useCurrentBasis: true);
        var growingRow = Assert.Single(growingRows, r => r.CustomerId == _freqGrowing);
        Assert.Equal(0, growingRow.PreviousFrequency);
        Assert.Equal(4, growingRow.CurrentFrequency);
        Assert.Null(growingRow.FrequencyDeltaPercent); // previous=0 -> "—", never a divide-by-zero value

        var decliningRows = await Fetch(FrequencyAudienceKey.Declining, useCurrentBasis: true);
        var decliningRow = Assert.Single(decliningRows, r => r.CustomerId == _freqDeclining);
        Assert.Equal(4, decliningRow.PreviousFrequency);
        Assert.Equal(1, decliningRow.CurrentFrequency);
        // Signed, matching FrequencyDeltaAbsolute's own sign convention (analysis doc §21:
        // frequency_delta_pct = frequency_delta / previous_frequency * 100) — NOT the same as
        // the classifier's internal (always-positive) decline-threshold percentage.
        Assert.Equal(-75m, decliningRow.FrequencyDeltaPercent!.Value); // (1-4)/4*100
        Assert.Equal(-3, decliningRow.FrequencyDeltaAbsolute);

        // Cross-check every classified audience against the pure FrequencyAudienceClassifier.
        foreach (var row in sleepingRows)
            Assert.Equal(FrequencyAudienceKey.Sleeping, FrequencyAudienceClassifier.Classify(row.PreviousFrequency, row.CurrentFrequency, declineThreshold));
        foreach (var row in growingRows)
            Assert.Equal(FrequencyAudienceKey.Growing, FrequencyAudienceClassifier.Classify(row.PreviousFrequency, row.CurrentFrequency, declineThreshold));
        foreach (var row in decliningRows)
            Assert.Equal(FrequencyAudienceKey.Declining, FrequencyAudienceClassifier.Classify(row.PreviousFrequency, row.CurrentFrequency, declineThreshold));

        // Nullable-parameter path: an explicit spend filter (non-null) that only Sleeping's
        // previous spend (300 = 3 receipts * 100) can satisfy, re-oriented via useCurrentBasis=false.
        var spendFiltered = await Fetch(FrequencyAudienceKey.Sleeping, useCurrentBasis: false, minSpend: 250m, maxSpend: 350m);
        Assert.Contains(spendFiltered, r => r.CustomerId == _freqSleeping);

        var spendExcluded = await Fetch(FrequencyAudienceKey.Sleeping, useCurrentBasis: false, minSpend: 9000m);
        Assert.DoesNotContain(spendExcluded, r => r.CustomerId == _freqSleeping);
    }

    // ── Settings CRUD ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_CRUD_round_trips_through_the_real_DbSet()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PriceSegmentsRepository(db);

        Assert.Null(await repo.GetSettingsAsync(_tenantId));

        var settings = new ShelfGuard.Domain.Entities.PriceSegmentSettings
        {
            TenantId = _tenantId,
            DefaultFrequencyDeclineThresholdPercent = 42m,
            MinReceiptsForBoundaries = 3,
        };
        await repo.AddSettingsAsync(settings);
        await repo.SaveChangesAsync();

        var reloaded = await repo.GetSettingsAsync(_tenantId);
        Assert.NotNull(reloaded);
        Assert.Equal(42m, reloaded!.DefaultFrequencyDeclineThresholdPercent);

        reloaded.DefaultFrequencyDeclineThresholdPercent = 55m;
        repo.UpdateSettings(reloaded);
        await repo.SaveChangesAsync();

        await using var db2 = NewContext();
        var repo2 = new PriceSegmentsRepository(db2);
        var updated = await repo2.GetSettingsAsync(_tenantId);
        Assert.Equal(55m, updated!.DefaultFrequencyDeclineThresholdPercent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private AppDbContext NewContext()
    {
        _options ??= new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build())
            .ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new AppDbContext(_options);
    }
}
