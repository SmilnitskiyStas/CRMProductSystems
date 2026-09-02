using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-429: live-Postgres coverage for <see cref="AudienceBuilderRepository"/> — the third raw-
/// SQL repository in this codebase, and the first one to build a multi-array
/// <c>UNNEST({n}::int[], {m}::text[])</c> term-matching query and a set-difference (competitor
/// MINUS own) query. A mocked/NSubstitute unit test structurally cannot verify any of this (the
/// UNNEST zip itself, the AND/OR covered_terms semantics, the double-counting fix for an item that
/// matches more than one term simultaneously, or the InPeriod-vs-AllTime competitor horizon
/// difference) — same rationale <c>PriceSegmentsRepositoryIntegrationTests</c> already established
/// for TASK-420. Same connection/skip pattern: real <c>crm</c> superuser connection, no RLS
/// session vars (SQL correctness under test, not tenant isolation — that is security-reviewer's
/// pass, not this task's).
///
/// Fixture shape (see InitializeAsync): "кола" matches TWO items on purpose (itemCola AND
/// itemComboBundle, a deliberate "unwanted bundle" case mirroring analysis doc §7.2/§24.2) so
/// manual-exclusion tests have something real to exclude. "Пепсі" (text) and the "Напої" category
/// both match itemPepsi (the SAME item matched by two different terms) — this is the fixture that
/// proves customer_totals is deduplicated (not double-counted) while customer_term_coverage still
/// correctly counts 2 covered terms from that single purchase. custCompetitorHistorical bought the
/// "own" item (cola) only OUTSIDE the active period — the fixture that distinguishes the InPeriod
/// vs AllTime competitor-exclusion horizons.
/// </summary>
public sealed class AudienceBuilderRepositoryIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    // platform_categories is global (B1) — a run token keeps this test's category name unique so
    // typeahead assertions are not disturbed by other tenants' / other tests' rows.
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _tenantId;
    private Guid _locationId;
    private Guid _catDrinksId;

    private Guid _itemCola, _itemComboBundle, _itemFanta, _itemPepsi, _itemSprite;

    private Guid _custColaOnly, _custFantaOnly, _custBoth, _custOutsidePeriod, _custOverlap, _custComboBuyer, _custCompetitorHistorical;

    private DateOnly _from, _to;
    private DateTime _periodAt, _periodAt2, _outsideAt;

    public AudienceBuilderRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping AudienceBuilderRepository integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _from = today.AddDays(-29);
        _to = today;
        _periodAt = _to.AddDays(-5).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        _periodAt2 = _to.AddDays(-4).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        _outsideAt = DateTime.UtcNow.AddDays(-500);

        await using var db = NewContext();

        var tenant = Tenant.Create($"AudienceBuilder Repo Test {Guid.NewGuid():N}", $"audience-builder-repo-test-{Guid.NewGuid():N}");
        _tenantId = tenant.Id;

        var location = new Location { TenantId = _tenantId, Name = "Test Store" };
        _locationId = location.Id;

        var catDrinks = new PlatformCategory { Name = $"Напої {_run}", IsActive = true };
        _catDrinksId = catDrinks.Id;

        var itemCola = new Item { TenantId = _tenantId, Name = "Кока-Кола 1,75л", ItemType = "product" };
        var itemComboBundle = new Item { TenantId = _tenantId, Name = "Подарунковий набір Кока-Кола", ItemType = "product" };
        var itemFanta = new Item { TenantId = _tenantId, Name = "Фанта 1л", ItemType = "product" };
        var itemPepsi = new Item { TenantId = _tenantId, Name = "Пепсі 1л", ItemType = "product", CategoryId = _catDrinksId };
        var itemSprite = new Item { TenantId = _tenantId, Name = "Спрайт 0,5л", ItemType = "product", CategoryId = _catDrinksId };
        _itemCola = itemCola.Id;
        _itemComboBundle = itemComboBundle.Id;
        _itemFanta = itemFanta.Id;
        _itemPepsi = itemPepsi.Id;
        _itemSprite = itemSprite.Id;

        db.Tenants.Add(tenant);
        db.Locations.Add(location);
        db.PlatformCategories.Add(catDrinks);
        db.Items.AddRange(itemCola, itemComboBundle, itemFanta, itemPepsi, itemSprite);

        Guid NewCustomer(string name, string? phone = null)
        {
            var c = new Customer { TenantId = _tenantId, Name = name, Phone = phone };
            db.Customers.Add(c);
            return c.Id;
        }

        _custColaOnly = NewCustomer("Клієнт Кола", "+380671110001");
        _custFantaOnly = NewCustomer("Клієнт Фанта"); // no phone -> WithPhoneCount coverage
        _custBoth = NewCustomer("Клієнт Обидва", "+380671110003");
        _custOutsidePeriod = NewCustomer("Клієнт Поза Періодом", "+380671110004");
        _custOverlap = NewCustomer("Клієнт Перетин", "+380671110005");
        _custComboBuyer = NewCustomer("Клієнт Комплект", "+380671110006");
        _custCompetitorHistorical = NewCustomer("Клієнт Конкурент Історія", "+380671110007");

        await db.SaveChangesAsync();

        var transactions = new List<PosTransaction>();
        var items = new List<PosTransactionItem>();
        var receiptSeq = 0;
        string NextReceipt() => $"AB-{++receiptSeq:0000}";

        void AddReceipt(Guid customerId, DateTime createdAtUtc, IReadOnlyList<(Guid ProductId, decimal Quantity, decimal PriceFinal)> lines)
        {
            var total = lines.Sum(l => l.Quantity * l.PriceFinal);
            var tx = new PosTransaction
            {
                TenantId = _tenantId,
                StoreId = _locationId,
                CustomerId = customerId,
                ReceiptNumber = NextReceipt(),
                TotalAmount = total,
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
        }

        // custColaOnly: 2 receipts, cola only, in period. qty=2 total, amount=105 total.
        AddReceipt(_custColaOnly, _periodAt, [(_itemCola, 1m, 50m)]);
        AddReceipt(_custColaOnly, _periodAt2, [(_itemCola, 1m, 55m)]);

        // custFantaOnly: 1 receipt, fanta only, in period. qty=3, amount=60.
        AddReceipt(_custFantaOnly, _periodAt, [(_itemFanta, 3m, 20m)]);

        // custBoth: 1 receipt, BOTH cola (qty1/amt50) and fanta (qty2/amt40) -> qty=3, amount=90.
        AddReceipt(_custBoth, _periodAt, [(_itemCola, 1m, 50m), (_itemFanta, 2m, 20m)]);

        // custOutsidePeriod: cola, 500 days ago -> must never appear in a period-scoped result.
        AddReceipt(_custOutsidePeriod, _outsideAt, [(_itemCola, 5m, 50m)]);

        // custOverlap: pepsi only, in period -> matches BOTH Text("Пепсі") AND Category(catDrinks)
        // simultaneously. qty=3, amount=30 -- must NOT become 6/60 under AND mode.
        AddReceipt(_custOverlap, _periodAt, [(_itemPepsi, 3m, 10m)]);

        // custComboBuyer: combo bundle only, in period -> also matches "кола". qty=1, amount=999.
        AddReceipt(_custComboBuyer, _periodAt, [(_itemComboBundle, 1m, 999m)]);

        // custCompetitorHistorical: fanta IN period (qty=1, amount=20) + cola OUTSIDE period
        // (qty=1, amount=50) -> distinguishes InPeriod vs AllTime competitor-exclusion horizons.
        AddReceipt(_custCompetitorHistorical, _periodAt, [(_itemFanta, 1m, 20m)]);
        AddReceipt(_custCompetitorHistorical, _outsideAt, [(_itemCola, 1m, 50m)]);

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
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform_categories WHERE \"Id\" = {_catDrinksId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    // ── Categories typeahead ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchCategoriesAsync_returns_item_count_and_respects_search_and_limit()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        // Search by the run token — unique to this test's category in the global table.
        var bySearch = await repo.SearchCategoriesAsync(_tenantId, _run[..8], 20);
        var row = Assert.Single(bySearch);
        Assert.Equal(_catDrinksId, row.CategoryId);
        Assert.Equal(2, row.ItemCount); // itemPepsi + itemSprite (ItemCount is tenant-scoped)

        // Empty search returns the whole global list; a generous limit keeps this category in it.
        var noSearch = await repo.SearchCategoriesAsync(_tenantId, "", 1000);
        Assert.Contains(noSearch, c => c.CategoryId == _catDrinksId);

        var noMatch = await repo.SearchCategoriesAsync(_tenantId, "zzz-no-such-category", 20);
        Assert.Empty(noMatch);
    }

    // ── Overview: OR audience, exclusion, and the double-counting fix ───────────────────────

    [Fact]
    public async Task GetOverviewAsync_single_term_matches_two_items_and_sums_correctly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола"], matchAll: false);
        var row = await repo.GetOverviewAsync(query);

        // "кола" matches itemCola AND itemComboBundle (both names contain "Кола").
        Assert.Equal(2, row.ItemsInSelectionCount);
        // Buyers of either matched item, in period: custColaOnly, custBoth (cola line only), custComboBuyer.
        Assert.Equal(3, row.ParticipantsCount);
        Assert.Equal(2m + 1m + 1m, row.UnitsPurchased); // custColaOnly(2) + custBoth(1, cola line only) + custComboBuyer(1)
        Assert.Equal(105m + 50m + 999m, row.TotalSpend);
    }

    [Fact]
    public async Task GetOverviewAsync_excluding_one_matched_item_removes_its_sole_buyer()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола"], matchAll: false, excludedItemIds: [_itemComboBundle]);
        var row = await repo.GetOverviewAsync(query);

        Assert.Equal(1, row.ItemsInSelectionCount); // only itemCola remains
        Assert.Equal(2, row.ParticipantsCount);      // custComboBuyer's only match was excluded
        Assert.Equal(3m, row.UnitsPurchased);        // custColaOnly(2) + custBoth(1)
        Assert.Equal(155m, row.TotalSpend);          // 105 + 50
    }

    [Fact]
    public async Task GetOverviewAsync_item_matching_two_terms_counts_once_toward_totals_but_covers_both_terms()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        // itemPepsi matches BOTH Text("Пепсі") (term 0) AND Category(catDrinks) (term 1).
        var query = new ResolvedAudienceQuery(
            _tenantId, ToUtcFrom(), ToUtcTo(), [],
            TextTermIndexes: [0], TextTermValues: ["Пепсі"],
            CategoryTermIndexes: [1], CategoryTermIds: [_catDrinksId],
            TermCount: 2, MatchAll: true, MinQuantity: null, MinAmount: null, ExcludedItemIds: []);

        var row = await repo.GetOverviewAsync(query);

        // AND mode: custOverlap's single Pepsi purchase satisfies BOTH term_index 0 and 1
        // simultaneously -> must still qualify (covered_terms = 2 = termCount).
        Assert.Equal(1, row.ParticipantsCount);
        // Regression guard for the design doc's own SQL sketch bug: qty must be 3 (the real
        // purchase), never 6 (double-counted once per matching term).
        Assert.Equal(3m, row.UnitsPurchased);
        Assert.Equal(30m, row.TotalSpend);

        var buyers = await repo.GetBuyersAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);
        var onlyRow = Assert.Single(buyers);
        Assert.Equal(_custOverlap, onlyRow.CustomerId);
        Assert.Equal(3m, onlyRow.QuantityPurchased);
        Assert.Equal(30m, onlyRow.TotalAmount);
        Assert.Equal(1, onlyRow.ReceiptCount);
    }

    // ── Buyers: OR vs AND, thresholds, sorting/pagination, phone/WithPhoneCount ─────────────

    [Fact]
    public async Task GetBuyersAsync_or_mode_unions_buyers_of_either_term_and_and_mode_requires_both()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var orQuery = TextQuery(["кола", "фанта"], matchAll: false);
        var orRows = await repo.GetBuyersAsync(orQuery, page: 1, pageSize: 10, sortBy: null, sortDescending: true);
        // custColaOnly/custComboBuyer (кола), custFantaOnly (фанта), custBoth (both), and
        // custCompetitorHistorical (their IN-PERIOD fanta purchase alone is enough for OR — their
        // cola purchase outside the period is irrelevant here, only relevant to the competitor
        // tests below).
        Assert.Equal(5, orRows.Count);
        Assert.Equal(5, orRows[0].TotalCount);
        Assert.Contains(orRows, r => r.CustomerId == _custColaOnly);
        Assert.Contains(orRows, r => r.CustomerId == _custFantaOnly);
        Assert.Contains(orRows, r => r.CustomerId == _custBoth);
        Assert.Contains(orRows, r => r.CustomerId == _custComboBuyer);
        Assert.Contains(orRows, r => r.CustomerId == _custCompetitorHistorical);
        // WithPhoneCount: every OR-audience member EXCEPT custFantaOnly (no phone) has a phone.
        Assert.Equal(4, orRows[0].WithPhoneCount);

        var andQuery = TextQuery(["кола", "фанта"], matchAll: true);
        var andRows = await repo.GetBuyersAsync(andQuery, page: 1, pageSize: 10, sortBy: null, sortDescending: true);
        var andRow = Assert.Single(andRows); // only custBoth covers BOTH term_index 0 and 1
        Assert.Equal(_custBoth, andRow.CustomerId);
        Assert.Equal(3m, andRow.QuantityPurchased); // cola(1) + fanta(2)
        Assert.Equal(90m, andRow.TotalAmount);
        Assert.Equal(1, andRow.ReceiptCount);
    }

    [Fact]
    public async Task GetBuyersAsync_min_quantity_threshold_filters_correctly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола", "фанта"], matchAll: false, minQuantity: 3m);
        var rows = await repo.GetBuyersAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);

        // custFantaOnly(qty=3) and custBoth(qty=3) pass; custColaOnly(qty=2) and custComboBuyer(qty=1) don't.
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.CustomerId == _custFantaOnly);
        Assert.Contains(rows, r => r.CustomerId == _custBoth);
    }

    [Fact]
    public async Task GetBuyersAsync_never_includes_purchases_outside_the_period()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола"], matchAll: false);
        var rows = await repo.GetBuyersAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);

        Assert.DoesNotContain(rows, r => r.CustomerId == _custOutsidePeriod);
    }

    [Fact]
    public async Task GetBuyersAsync_pagination_and_sort_by_name_are_stable()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола", "фанта"], matchAll: false);

        var page1 = await repo.GetBuyersAsync(query, page: 1, pageSize: 2, sortBy: "name", sortDescending: false);
        var page2 = await repo.GetBuyersAsync(query, page: 2, pageSize: 2, sortBy: "name", sortDescending: false);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Equal(5, page1[0].TotalCount); // see GetBuyersAsync_or_mode_... for why 5, not 4
        Assert.True(string.CompareOrdinal(page1[0].Name, page1[1].Name) <= 0);
        Assert.True(string.CompareOrdinal(page1[1].Name, page2[0].Name) <= 0);
        Assert.DoesNotContain(page2, r => page1.Any(p => p.CustomerId == r.CustomerId));
    }

    // ── Matched items: zero-sales inclusion, exclusion flag, barcode-less items ──────────────

    [Fact]
    public async Task GetMatchedItemsAsync_shows_sales_stats_and_exclusion_flag_for_a_text_term()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола"], matchAll: false, excludedItemIds: [_itemComboBundle]);
        var rows = await repo.GetMatchedItemsAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);

        Assert.Equal(2, rows.Count);
        var cola = rows.Single(r => r.ItemId == _itemCola);
        Assert.False(cola.IsExcluded);
        Assert.Equal(3m, cola.QuantitySold);   // custColaOnly(2) + custBoth(1)
        Assert.Equal(3, cola.ReceiptCount);    // 2 + 1 receipts
        Assert.Equal(2, cola.BuyerCount);      // custColaOnly, custBoth
        Assert.Null(cola.BarcodesJoined);      // no barcodes on the fixture item

        var combo = rows.Single(r => r.ItemId == _itemComboBundle);
        Assert.True(combo.IsExcluded);         // reflects the caller's current exclusion list
        Assert.Equal(1m, combo.QuantitySold);  // still reports real sales even though excluded
        Assert.Equal(1, combo.BuyerCount);
    }

    [Fact]
    public async Task GetMatchedItemsAsync_includes_zero_sales_items_matched_via_category()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = new ResolvedAudienceQuery(
            _tenantId, ToUtcFrom(), ToUtcTo(), [],
            TextTermIndexes: [], TextTermValues: [],
            CategoryTermIndexes: [0], CategoryTermIds: [_catDrinksId],
            TermCount: 1, MatchAll: false, MinQuantity: null, MinAmount: null, ExcludedItemIds: []);

        var rows = await repo.GetMatchedItemsAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);

        Assert.Equal(2, rows.Count); // itemPepsi (has sales) + itemSprite (zero sales, still listed)
        var sprite = rows.Single(r => r.ItemId == _itemSprite);
        Assert.Equal(0m, sprite.QuantitySold);
        Assert.Equal(0, sprite.ReceiptCount);
        Assert.Equal(0, sprite.BuyerCount);
        Assert.False(sprite.IsExcluded);

        var pepsi = rows.Single(r => r.ItemId == _itemPepsi);
        Assert.Equal(3m, pepsi.QuantitySold);
    }

    // ── Receipt-level export ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBuyerReceiptsAsync_returns_one_row_per_receipt_scoped_to_matched_skus_only()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = TextQuery(["кола"], matchAll: false);
        var rows = await repo.GetBuyerReceiptsAsync(query, maxRows: 1000);

        // custColaOnly: 2 receipts. custBoth: 1 receipt (cola line only, fanta excluded from the
        // reported amount/qty since "кола" doesn't match fanta). custComboBuyer: 1 receipt.
        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Count(r => r.CustomerId == _custColaOnly));
        Assert.All(rows, r => Assert.Equal("Test Store", r.StoreName));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.ReceiptNumber)));

        var custBothRow = rows.Single(r => r.CustomerId == _custBoth);
        Assert.Equal(1m, custBothRow.Quantity); // only the cola line, not the fanta line on the same receipt
        Assert.Equal(50m, custBothRow.Amount);

        var comboAmounts = rows.Where(r => r.CustomerId == _custColaOnly).Select(r => r.Amount).OrderBy(a => a).ToList();
        Assert.Equal([50m, 55m], comboAmounts);
    }

    // ── Competitor audience: InPeriod vs AllTime horizon, own-side exclusion ────────────────

    [Fact]
    public async Task GetCompetitorOverviewAsync_in_period_horizon_excludes_only_customers_who_bought_own_item_in_period()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = CompetitorQuery(ownTerm: "кола", competitorTerm: "фанта", allTime: false);
        var row = await repo.GetCompetitorOverviewAsync(query);

        // Fanta-in-period buyers: custFantaOnly, custBoth, custCompetitorHistorical.
        // Excluded (bought cola IN period): custBoth only -> custCompetitorHistorical (cola only
        // OUTSIDE the period) is NOT excluded under this horizon.
        Assert.Equal(2, row.NewAudienceCount);
        Assert.Equal(1, row.CompetitorItemsCount); // itemFanta only
        Assert.Equal(3m + 1m, row.UnitsPurchased); // custFantaOnly(3) + custCompetitorHistorical(1)
        Assert.Equal(60m + 20m, row.TotalSpend);

        var buyers = await repo.GetCompetitorBuyersAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);
        Assert.Equal(2, buyers.Count);
        Assert.Contains(buyers, b => b.CustomerId == _custFantaOnly);
        Assert.Contains(buyers, b => b.CustomerId == _custCompetitorHistorical);
        Assert.DoesNotContain(buyers, b => b.CustomerId == _custBoth);
    }

    [Fact]
    public async Task GetCompetitorOverviewAsync_all_time_horizon_also_excludes_historical_own_buyers()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var query = CompetitorQuery(ownTerm: "кола", competitorTerm: "фанта", allTime: true);
        var row = await repo.GetCompetitorOverviewAsync(query);

        // AllTime horizon additionally excludes custCompetitorHistorical (bought cola OUTSIDE the
        // period, but "ever" still disqualifies them) -> only custFantaOnly remains.
        Assert.Equal(1, row.NewAudienceCount);
        Assert.Equal(3m, row.UnitsPurchased);
        Assert.Equal(60m, row.TotalSpend);

        var buyers = await repo.GetCompetitorBuyersAsync(query, page: 1, pageSize: 10, sortBy: null, sortDescending: true);
        var only = Assert.Single(buyers);
        Assert.Equal(_custFantaOnly, only.CustomerId);
    }

    [Fact]
    public async Task GetCompetitorOverviewAsync_excluding_the_only_own_matched_item_disables_the_exclusion_entirely()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new AudienceBuilderRepository(db);

        var baseQuery = CompetitorQuery(ownTerm: "кола", competitorTerm: "фанта", allTime: false);
        var query = baseQuery with { OwnExcludedItemIds = [_itemCola] };
        var row = await repo.GetCompetitorOverviewAsync(query);

        // With the sole own-matching item excluded, own_matched is empty -> nobody is excluded ->
        // ALL 3 fanta-in-period buyers become "new audience", including custBoth.
        Assert.Equal(3, row.NewAudienceCount);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private DateTime ToUtcFrom() => _from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    private DateTime ToUtcTo() => _to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

    private ResolvedAudienceQuery TextQuery(
        IReadOnlyList<string> terms, bool matchAll, decimal? minQuantity = null, decimal? minAmount = null, Guid[]? excludedItemIds = null) =>
        new(
            _tenantId, ToUtcFrom(), ToUtcTo(), [],
            TextTermIndexes: Enumerable.Range(0, terms.Count).ToArray(),
            TextTermValues: terms.ToArray(),
            CategoryTermIndexes: [], CategoryTermIds: [],
            TermCount: terms.Count, MatchAll: matchAll,
            MinQuantity: minQuantity, MinAmount: minAmount, ExcludedItemIds: excludedItemIds ?? []);

    private ResolvedCompetitorQuery CompetitorQuery(string ownTerm, string competitorTerm, bool allTime) => new(
        _tenantId, ToUtcFrom(), ToUtcTo(), [],
        CompetitorTextTermIndexes: [0], CompetitorTextTermValues: [competitorTerm],
        CompetitorCategoryTermIndexes: [], CompetitorCategoryTermIds: [],
        OwnTextTermIndexes: [0], OwnTextTermValues: [ownTerm],
        OwnCategoryTermIndexes: [], OwnCategoryTermIds: [],
        OwnExcludedItemIds: [], AllTimeHorizon: allTime);

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
