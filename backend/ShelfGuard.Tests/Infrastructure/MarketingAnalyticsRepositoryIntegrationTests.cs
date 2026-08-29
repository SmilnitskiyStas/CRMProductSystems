using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-406: live-Postgres coverage for MarketingAnalyticsRepository — the first raw-SQL
/// repository in this codebase (NTILE window functions, array parameters, Europe/Kyiv timezone
/// conversion). A mocked/NSubstitute unit test structurally cannot verify any of this — same
/// rationale as LoyaltyRepositoryIntegrationTests for TryClaimTimestepAsync. Same
/// connection/skip pattern (real `crm` superuser connection, no RLS session vars set — the
/// scenario under test is SQL correctness, not tenant isolation).
///
/// Fixture shape (see InitializeAsync): 4 customers designed to exercise the trickiest
/// distinctions the SQL has to get right —
///   CustomerA "Активний"   — 5 receipts in the last 10 days (high recency/frequency/monetary)
///   CustomerC "Змішаний"   — 1 receipt 400 days ago + 1 receipt 5 days ago (proves
///                            PERIOD-scoped receipt_count/revenue differ from LIFETIME
///                            first/last-purchase-date/receipt-count within the SAME row)
///   CustomerB "Одноразовий" — a single receipt 400 days ago (outside the narrow 30-day scoring
///                            window used below — proves such a customer is correctly ABSENT
///                            from GetScoredCustomersAsync's result for that window)
///   CustomerD "БезПокупок" — zero transactions (proves GetCustomerBaseCountsAsync's
///                            registered-vs-ever-purchased split)
/// Items: "Банан"/"Йогурт" (co-purchased by CustomerA, for affinity/basket), a SECOND "Банан"
/// Item row (different Id, same Name, bought by CustomerC) to prove name-based de-duplication,
/// and "Коробка" (ItemType="packaging") to prove the packaging exclusion.
/// </summary>
public sealed class MarketingAnalyticsRepositoryIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _locationId;
    private Guid _locationId2, _locationId3; // TASK-502: store-migration fixture, 2 extra stores
    private Guid _bananaItemId1;
    private Guid _bananaItemId2; // same Name, different Id — proves name-based grouping
    private Guid _yogurtItemId;
    private Guid _packagingItemId;
    private Guid _customerA, _customerB, _customerC, _customerD;
    private Guid _customerE, _customerF; // TASK-502: store-migration fixture

    public MarketingAnalyticsRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping MarketingAnalyticsRepository integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();
        var now = DateTime.UtcNow;

        var tenant = Tenant.Create($"RFM Repo Test {Guid.NewGuid():N}", $"rfm-repo-test-{Guid.NewGuid():N}");
        _tenantId = tenant.Id;

        var location = new Location { TenantId = _tenantId, Name = "Test Store" };
        var location2 = new Location { TenantId = _tenantId, Name = "Store B" };
        var location3 = new Location { TenantId = _tenantId, Name = "Store C" };
        _locationId = location.Id;
        _locationId2 = location2.Id;
        _locationId3 = location3.Id;

        var banana1 = new Item { TenantId = _tenantId, Name = "Банан", ItemType = "product" };
        var banana2 = new Item { TenantId = _tenantId, Name = "Банан", ItemType = "product" }; // dup by name, different Id
        var yogurt = new Item { TenantId = _tenantId, Name = "Йогурт", ItemType = "product" };
        var packaging = new Item { TenantId = _tenantId, Name = "Коробка", ItemType = "packaging" };
        _bananaItemId1 = banana1.Id;
        _bananaItemId2 = banana2.Id;
        _yogurtItemId = yogurt.Id;
        _packagingItemId = packaging.Id;

        var custA = new Customer { TenantId = _tenantId, Name = "Активний" };
        var custB = new Customer { TenantId = _tenantId, Name = "Одноразовий" };
        var custC = new Customer { TenantId = _tenantId, Name = "Змішаний", Phone = "+380671234567" };
        var custD = new Customer { TenantId = _tenantId, Name = "БезПокупок" };
        var custE = new Customer { TenantId = _tenantId, Name = "ОдинЗаклад" };
        var custF = new Customer { TenantId = _tenantId, Name = "ТриЗаклади" };
        _customerA = custA.Id;
        _customerB = custB.Id;
        _customerC = custC.Id;
        _customerD = custD.Id;
        _customerE = custE.Id;
        _customerF = custF.Id;

        db.Tenants.Add(tenant);
        db.Locations.AddRange(location, location2, location3);
        db.Items.AddRange(banana1, banana2, yogurt, packaging);
        db.Customers.AddRange(custA, custB, custC, custD, custE, custF);
        await db.SaveChangesAsync();

        var transactions = new List<PosTransaction>();
        var items = new List<PosTransactionItem>();
        var receiptSeq = 0;
        string NextReceipt() => $"RFM-{++receiptSeq:0000}";

        void AddReceipt(Guid customerId, DateTime createdAtUtc, decimal totalAmount, IReadOnlyList<Guid> productIds, Guid? storeId = null)
        {
            var tx = new PosTransaction
            {
                TenantId = _tenantId,
                StoreId = storeId ?? _locationId,
                CustomerId = customerId,
                ReceiptNumber = NextReceipt(),
                TotalAmount = totalAmount,
                CreatedAt = createdAtUtc,
            };
            transactions.Add(tx);
            foreach (var productId in productIds)
            {
                items.Add(new PosTransactionItem
                {
                    TransactionId = tx.Id,
                    ProductId = productId,
                    Quantity = 1,
                    PriceRetail = totalAmount,
                    DiscountAmount = 0,
                    PriceFinal = totalAmount,
                });
            }
        }

        // CustomerA: 5 receipts across the last 10 days, banana+yogurt together (4 of them),
        // 1 also includes packaging (must never surface in top-products/affinity/basket).
        for (var i = 1; i <= 4; i++)
            AddReceipt(_customerA, now.AddDays(-i), 500m, [_bananaItemId1, _yogurtItemId]);
        AddReceipt(_customerA, now.AddDays(-1).AddHours(-1), 500m, [_bananaItemId1, _yogurtItemId, _packagingItemId]);

        // Timezone probe receipt for CustomerA — firmly inside Ukraine's EEST (UTC+3) DST
        // window, used only by the behavior/day-hour test below (kept separate from the
        // "last 10 days" window so it doesn't skew the recency-ordering assertions).
        var probeUtc = new DateTime(2026, 6, 15, 22, 0, 0, DateTimeKind.Utc);
        AddReceipt(_customerA, probeUtc, 500m, [_bananaItemId1]);

        // CustomerB: single receipt 400 days ago — outside the narrow 30-day scoring window
        // used below, so must be ABSENT from that window's GetScoredCustomersAsync result.
        AddReceipt(_customerB, now.AddDays(-400), 50m, [_yogurtItemId]);

        // CustomerC: one very old receipt (400 days ago, banana2 — the duplicate-name Item) and
        // one recent receipt (5 days ago) inside the narrow window — proves period-scoped vs
        // lifetime fields diverge correctly on the SAME customer.
        AddReceipt(_customerC, now.AddDays(-400), 80m, [_bananaItemId2]);
        AddReceipt(_customerC, now.AddDays(-5), 300m, [_bananaItemId2]);

        // CustomerD gets no transactions at all (the "no purchase" case).

        // ── Store migration fixture (TASK-502) ──────────────────────────────────────────────
        // CustomerE: 2 receipts in-period, both at the SAME store — must be excluded from
        // migration flows/customers entirely (not a migration).
        AddReceipt(_customerE, now.AddDays(-15), 100m, [], _locationId);
        AddReceipt(_customerE, now.AddDays(-5), 150m, [], _locationId);

        // CustomerF: 3 receipts in-period across 3 DISTINCT stores in chronological order —
        // first-in-period is _locationId, middle is _locationId2 (must be ignored entirely),
        // last-in-period is _locationId3. Migration must resolve from=_locationId, to=_locationId3.
        AddReceipt(_customerF, now.AddDays(-20), 200m, [], _locationId);
        AddReceipt(_customerF, now.AddDays(-10), 300m, [], _locationId2);
        AddReceipt(_customerF, now.AddDays(-3), 400m, [], _locationId3);

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
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetScoredCustomersAsync_narrow_window_excludes_customerB_and_separates_period_from_lifetime_facts()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rows = await repo.GetScoredCustomersAsync(_tenantId, null, today.AddDays(-30), today);

        // CustomerB's only receipt is 400 days ago — must not appear in a 30-day window at all.
        Assert.DoesNotContain(rows, r => r.CustomerId == _customerB);

        var a = rows.Single(r => r.CustomerId == _customerA);
        var c = rows.Single(r => r.CustomerId == _customerC);

        // Period-scoped: A has 5 receipts in-window ("recent" ones only — the timezone-probe
        // receipt is a fixed 2026-06-15 calendar date, deliberately outside any "last 30 days"
        // window from here on; see GetBehaviorAsync_.../day boundary test for that receipt),
        // C has exactly 1 (the 400-day-old one falls outside this window).
        Assert.Equal(5, a.PeriodReceiptCount);
        Assert.Equal(1, c.PeriodReceiptCount);
        Assert.Equal(300m, c.PeriodRevenue);

        // Lifetime facts diverge from the period for C: 2 receipts total, first 400 days ago,
        // last 5 days ago — none of which match its PERIOD receipt count of 1.
        Assert.Equal(2, c.LifetimeReceiptCount);
        Assert.Equal(today.AddDays(-400), c.FirstPurchaseDateEver);
        Assert.Equal(today.AddDays(-5), c.LastPurchaseDateEver);

        // A bought more, more recently, for more money in-window than C — scores must reflect
        // that ordering (asserting ORDER, not exact bucket numbers: NTILE(5) over a 2-row
        // population never fills all 5 buckets, so exact-value assertions would be fragile).
        Assert.True(a.RecencyScore >= c.RecencyScore);
        Assert.True(a.FrequencyScore >= c.FrequencyScore);
        Assert.True(a.MonetaryScore >= c.MonetaryScore);
        Assert.InRange(a.RecencyScore, 1, 5);
        Assert.InRange(a.FrequencyScore, 1, 5);
        Assert.InRange(a.MonetaryScore, 1, 5);
    }

    [Fact]
    public async Task GetCustomerBaseCountsAsync_splits_registered_vs_ever_purchased()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));

        var counts = await repo.GetCustomerBaseCountsAsync(_tenantId, null);

        // TASK-502 added CustomerE/F to the shared fixture (store-migration tests) — both have
        // receipts, so they count toward EverPurchasedCount same as A/B/C; only D never purchased.
        Assert.Equal(6, counts.RegisteredCount); // A, B, C, D, E, F
        Assert.Equal(5, counts.EverPurchasedCount); // A, B, C, E, F — D never purchased
    }

    [Fact]
    public async Task GetTopProductsAsync_excludes_packaging_and_groups_duplicate_names()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Wide window + both customers as "the segment" — proves the two differently-Id'd
        // "Банан" Item rows (bought by A and C respectively) collapse into ONE product row.
        var segmentIds = new[] { _customerA, _customerC };
        var rows = await repo.GetTopProductsAsync(_tenantId, segmentIds, null, today.AddDays(-410), today, limit: 10);

        Assert.DoesNotContain(rows, r => r.ProductName == "Коробка");

        var banana = rows.Single(r => r.ProductName == "Банан");
        Assert.Equal(2, banana.UniqueCustomerCount); // A (bananaItemId1) + C (bananaItemId2) merged by name

        var yogurt = rows.Single(r => r.ProductName == "Йогурт");
        Assert.Equal(1, yogurt.UniqueCustomerCount); // only A bought yogurt
    }

    [Fact]
    public async Task GetLtvAsync_is_all_time_regardless_of_any_window()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));

        // CustomerC's lifetime total is 80 (400 days ago) + 300 (5 days ago) = 380, even though
        // GetLtvAsync takes no date-range parameter at all — the method signature itself proves
        // there's no window to accidentally apply.
        var ltv = await repo.GetLtvAsync(_tenantId, [_customerC], null);

        Assert.Equal(380m, ltv.TotalLtv);
        Assert.Equal(380m, ltv.AverageLtv); // single customer in this call
    }

    [Fact]
    public async Task GetAffinityAsync_and_GetBasketAsync_exclude_packaging_and_find_the_co_purchased_product()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var segmentIds = new[] { _customerA };
        var from = today.AddDays(-30);

        var affinity = await repo.GetAffinityAsync(
            _tenantId, segmentIds, null, from, today, anchorProductName: "Банан",
            candidatePoolSize: 200, topN: 10);
        Assert.DoesNotContain(affinity, r => r.ProductName == "Коробка");
        Assert.Contains(affinity, r => r.ProductName == "Йогурт");

        var basket = await repo.GetBasketAsync(
            _tenantId, segmentIds, null, from, today, anchorProductName: "Банан", topN: 10);
        Assert.DoesNotContain(basket, r => r.ProductName == "Коробка");
        var yogurtBasket = basket.Single(r => r.ProductName == "Йогурт");
        // All 5 of Banana's in-window receipts also contain yogurt (the packaging receipt still
        // has yogurt too — only the out-of-window timezone-probe receipt is banana-only).
        Assert.Equal(5, yogurtBasket.BothReceiptsCount);
    }

    [Fact]
    public async Task GetBehaviorAsync_converts_to_Europe_Kyiv_correctly_across_a_day_boundary()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Isolate the single timezone-probe receipt (2026-06-15 22:00 UTC) via a tight window
        // that excludes every other seeded receipt, so the day/hour distribution is unambiguous.
        var probeDate = DateOnly.FromDateTime(new DateTime(2026, 6, 15));
        var behavior = await repo.GetBehaviorAsync(
            _tenantId, [_customerA], null, probeDate.AddDays(-1), probeDate.AddDays(1));

        Assert.Equal(1, behavior.ReceiptCount);

        // Hand-computed expectation via plain +3h arithmetic (Ukraine's EEST offset in June),
        // not a TimeZoneInfo lookup — see class doc for why. 22:00 UTC + 3h = 01:00 next day.
        var expectedKyivLocal = new DateTime(2026, 6, 15, 22, 0, 0, DateTimeKind.Utc).AddHours(3);
        var expectedIsoDow = (int)expectedKyivLocal.DayOfWeek == 0 ? 7 : (int)expectedKyivLocal.DayOfWeek;

        var dayRow = Assert.Single(behavior.ByDayOfWeek);
        Assert.Equal(expectedIsoDow, dayRow.DayOfWeekIso);
        Assert.Equal(1, dayRow.ReceiptCount);

        var hourRow = Assert.Single(behavior.ByHour);
        Assert.Equal(expectedKyivLocal.Hour, hourRow.Hour);
        Assert.Equal(1, hourRow.ReceiptCount);
    }

    [Fact]
    public async Task GetProductBuyerCustomerIdsAsync_and_PairBuyerCustomerIdsAsync_return_the_right_customers()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var segmentIds = new[] { _customerA, _customerC };

        var bananaBuyers = await repo.GetProductBuyerCustomerIdsAsync(
            _tenantId, segmentIds, null, today.AddDays(-410), today, "Банан");
        Assert.Equal(segmentIds.ToHashSet(), bananaBuyers.ToHashSet());

        var pairBuyers = await repo.GetProductPairBuyerCustomerIdsAsync(
            _tenantId, segmentIds, null, today.AddDays(-410), today, "Банан", "Йогурт");
        Assert.Equal([_customerA], pairBuyers); // only A bought both; C never bought yogurt
    }

    [Fact]
    public async Task GetExportCustomersAsync_returns_requested_customers_scoped_to_tenant()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));

        var rows = await repo.GetExportCustomersAsync(_tenantId, [_customerA, _customerC]);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.CustomerId == _customerA && r.Name == "Активний");
        var cRow = rows.Single(r => r.CustomerId == _customerC);
        Assert.Equal("+380671234567", cRow.Phone);
    }

    [Fact]
    public async Task GetStoreMigrationFlowsAsync_and_CustomersAsync_exclude_single_store_customer_and_resolve_first_last_ignoring_middle_store()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-30);

        var flows = await repo.GetStoreMigrationFlowsAsync(_tenantId, null, from, today);
        var customers = await repo.GetStoreMigrationCustomersAsync(_tenantId, null, from, today, limit: 100);

        // CustomerE never appears — both of its in-period receipts share the same store.
        Assert.DoesNotContain(customers, c => c.CustomerId == _customerE);

        // CustomerF resolves from=_locationId (earliest in-period receipt) to=_locationId3
        // (latest in-period receipt), ignoring the middle _locationId2 receipt entirely.
        var fRow = customers.Single(c => c.CustomerId == _customerF);
        Assert.Equal(_locationId, fRow.FromStoreId);
        Assert.Equal(_locationId3, fRow.ToStoreId);
        Assert.Equal(today.AddDays(-20), fRow.FromDate);
        Assert.Equal(today.AddDays(-3), fRow.ToDate);
        // All 3 in-period receipts count toward the aggregate, not just first/last.
        Assert.Equal(3, fRow.TransactionCountInPeriod);
        Assert.Equal(900m, fRow.RevenueInPeriod); // 200 + 300 + 400

        var flowCell = flows.Single(f => f.FromStoreId == _locationId && f.ToStoreId == _locationId3);
        Assert.Equal(1, flowCell.CustomerCount);
        Assert.Equal(900m, flowCell.Revenue);
    }

    [Fact]
    public async Task GetStoreMigrationFlowsAsync_store_filter_matches_on_either_from_or_to_store_but_not_the_ignored_middle_store()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-30);

        // Filtering to only the FROM store still surfaces the migration.
        var fromOnly = await repo.GetStoreMigrationFlowsAsync(_tenantId, [_locationId], from, today);
        Assert.Contains(fromOnly, f => f.FromStoreId == _locationId && f.ToStoreId == _locationId3);

        // Filtering to only the TO store still surfaces the same migration.
        var toOnly = await repo.GetStoreMigrationFlowsAsync(_tenantId, [_locationId3], from, today);
        Assert.Contains(toOnly, f => f.FromStoreId == _locationId && f.ToStoreId == _locationId3);

        // Filtering to the ignored MIDDLE store (never first-in-period or last-in-period for
        // anyone in the fixture) excludes CustomerF's migration entirely.
        var middleOnly = await repo.GetStoreMigrationFlowsAsync(_tenantId, [_locationId2], from, today);
        Assert.DoesNotContain(middleOnly, f => f.FromStoreId == _locationId && f.ToStoreId == _locationId3);
    }

    [Fact]
    public async Task GetActivePeriodCustomerCountAsync_counts_distinct_customers_with_a_receipt_in_the_window()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketingAnalyticsRepository(db, new AnalyticsRlsOverride(db));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Narrow window covering only CustomerE's and CustomerF's receipts (days -20..-3) —
        // A/B/C/D's receipts fall outside it (A's are within -1..-5, still inside; excluded via
        // a store filter instead to keep this assertion unambiguous).
        var count = await repo.GetActivePeriodCustomerCountAsync(
            _tenantId, [_locationId2, _locationId3], today.AddDays(-30), today);

        // Only CustomerF has a receipt at _locationId2 or _locationId3 in this fixture.
        Assert.Equal(1, count);
    }

    // KI-035: the ONE process-wide pooled NpgsqlDataSource/DbContextOptions, shared with every
    // other Postgres-backed integration-test class in this assembly (see TestPostgres). This file
    // already cached its data source statically rather than per-[Fact], so it was the mildest of
    // the leakers — but it still never disposed it, and sharing one pool assembly-wide is what
    // actually keeps the total backend count bounded. Sharing one DbContextOptions also
    // structurally removes the EF ManyServiceProvidersCreatedWarning pressure that this file's
    // previous comment (2026-08-19 CI fix) was about.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
