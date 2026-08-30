using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Pos;

/// <summary>
/// TASK-414 (security review TASK-412, finding B): real-Postgres concurrency test for the
/// lost-update race on <see cref="LoyaltyMembership.Balance"/> — two POS sales redeeming
/// against the SAME loyalty membership at the same moment via <see cref="Task.WhenAll"/>. Same
/// pattern as the sibling <see cref="PosConcurrencySalesIntegrationTests"/> (ProductStock,
/// TASK-356): two independent <see cref="PosService"/> instances, each backed by its own
/// <see cref="AppDbContext"/>, against the local dev Postgres, with a deterministic rendezvous
/// (not timing luck) forcing both to read the same pre-write Balance before either writes.
///
/// Deliberately uses TWO DIFFERENT products (each with ample, non-contended stock) so the
/// already-covered ProductStock race can never fire here — the only thing that can conflict in
/// this test is the shared <see cref="LoyaltyMembership"/> row, isolating the exact scenario
/// finding B describes: two registers redeeming bonus balance off the same membership at once.
///
/// Before TASK-414 (no concurrency token on LoyaltyMembership.Balance), both redemptions used to
/// succeed with a last-write-wins UPDATE, letting the customer redeem more than they actually
/// had. After: the loser's SaveChangesAsync throws on the xmin mismatch, LoyaltyRepository
/// translates that into ConcurrencyConflictException, and PosService.CreateSaleAsync turns it
/// into a clean 409 instead of corrupting Balance. Skips (soft-pass) when no reachable Postgres
/// is configured. Override via env var SHELFGUARD_TEST_DB_CONNECTION.
/// </summary>
public sealed class LoyaltyConcurrencySalesIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _membershipId;
    private Guid _shiftId;
    private Guid _consumerAccountId;
    private string _barcodeA = string.Empty;
    private string _barcodeB = string.Empty;

    private const decimal StartingBalance = 100m;
    private const decimal PriceRetail = 40m;
    private const decimal RedeemAmountPerSale = 40m;

    public LoyaltyConcurrencySalesIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping loyalty concurrency integration test — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        _barcodeA = $"CONC-LOY-A-{Guid.NewGuid():N}"[..20];
        _barcodeB = $"CONC-LOY-B-{Guid.NewGuid():N}"[..20];

        await using var db = NewContext();

        var tenant = Tenant.Create($"Loyalty Concurrency Test {Guid.NewGuid():N}", $"loyalty-conc-test-{Guid.NewGuid():N}");
        var store = new Location { TenantId = tenant.Id, Name = "Loyalty Concurrency Test Store" };
        var productA = new Item
        {
            TenantId = tenant.Id, Name = "Loyalty Concurrency Test Product A",
            Barcodes = [_barcodeA], PriceRetail = PriceRetail,
        };
        var productB = new Item
        {
            TenantId = tenant.Id, Name = "Loyalty Concurrency Test Product B",
            Barcodes = [_barcodeB], PriceRetail = PriceRetail,
        };
        var stockA = new ProductStock
        {
            TenantId = tenant.Id, ProductId = productA.Id, StoreId = store.Id,
            Quantity = 10m, QuantityInitial = 10m, // ample — never contended, isolates the test to the loyalty race
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Status = "safe",
        };
        var stockB = new ProductStock
        {
            TenantId = tenant.Id, ProductId = productB.Id, StoreId = store.Id,
            Quantity = 10m, QuantityInitial = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Status = "safe",
        };
        var shift = new PosShift { TenantId = tenant.Id, StoreId = store.Id, OpenedAt = DateTime.UtcNow };

        var consumerAccountId = Guid.NewGuid();
        var consumer = new ConsumerAccount
        {
            Id = consumerAccountId,
            Phone = $"+380{consumerAccountId:N}"[..13],
            PasswordHash = "x",
            FullName = "Loyalty Concurrency Test Consumer",
        };
        var membership = new LoyaltyMembership
        {
            TenantId = tenant.Id,
            ConsumerAccountId = consumerAccountId,
            TotpSecret = "SECRET",
            Balance = StartingBalance,
            Status = LoyaltyMembershipStatus.Active,
        };
        // AccrualRatePercent=0 keeps the expected final balance simple to reason about (pure
        // redemption, no accrual on top); RedemptionCapPercent=100 so a single sale can redeem
        // its whole TotalAmount without the cap check itself rejecting the request.
        var settings = new LoyaltyProgramSettings
        {
            TenantId = tenant.Id, IsEnabled = true, AccrualRatePercent = 0m,
            RedemptionCapPercent = 100m, MinRedemptionBalance = 0m,
        };

        db.Tenants.Add(tenant);
        db.Locations.Add(store);
        db.Items.AddRange(productA, productB);
        db.ProductStocks.AddRange(stockA, stockB);
        db.PosShifts.Add(shift);
        db.ConsumerAccounts.Add(consumer);
        db.LoyaltyMemberships.Add(membership);
        db.LoyaltyProgramSettings.Add(settings);
        await db.SaveChangesAsync();

        _tenantId = tenant.Id;
        _membershipId = membership.Id;
        _shiftId = shift.Id;
        _consumerAccountId = consumerAccountId;
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM loyalty_ledger_entries WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM loyalty_memberships WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM loyalty_program_settings WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM consumer_accounts WHERE \"Id\" = {_consumerAccountId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transaction_items WHERE \"ProductStockId\" IN (SELECT \"Id\" FROM product_stock WHERE \"TenantId\" = {_tenantId})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_events WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transactions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_shifts WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task Two_concurrent_redemptions_against_the_same_membership_never_overspend_the_balance()
    {
        if (!_dbAvailable)
        {
            _output.WriteLine("DB not available — skipped.");
            return;
        }

        await using var dbA = NewContext();
        await using var dbB = NewContext();

        // Same two-way rendezvous shape as PosConcurrencySalesIntegrationTests, but gated on the
        // loyalty membership read (PosService.CreateSaleAsync's GetMembershipByIdAsync) instead
        // of the FEFO stock read — both sales are guaranteed to read the same pre-write Balance
        // before either is allowed to proceed to its redemption check + write.
        var aHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var svcA = BuildService(dbA, loyalty => new RendezvousLoyaltyRepository(loyalty, SignalSelf: aHasRead, WaitFor: bHasRead.Task));
        var svcB = BuildService(dbB, loyalty => new RendezvousLoyaltyRepository(loyalty, SignalSelf: bHasRead, WaitFor: aHasRead.Task, WaitBeforeRead: true));

        var requestA = new CreateSaleRequest(
            ShiftId: _shiftId, Items: [new SaleItemRequest(_barcodeA, 1)], PaymentType: "Cash",
            PaymentAmount: PriceRetail, LoyaltyMembershipId: _membershipId, RedeemAmount: RedeemAmountPerSale);
        var requestB = new CreateSaleRequest(
            ShiftId: _shiftId, Items: [new SaleItemRequest(_barcodeB, 1)], PaymentType: "Cash",
            PaymentAmount: PriceRetail, LoyaltyMembershipId: _membershipId, RedeemAmount: RedeemAmountPerSale);

        var taskA = svcA.CreateSaleAsync(_tenantId, Guid.NewGuid(), requestA);
        var taskB = svcB.CreateSaleAsync(_tenantId, Guid.NewGuid(), requestB);

        var results = await Task.WhenAll(taskA, taskB);

        _output.WriteLine(string.Join(" | ", results.Select(r =>
            r.Error is null
                ? $"OK sale={r.Sale!.TransactionId} balanceAfter={r.Sale.LoyaltyBalance}"
                : $"ERROR status={r.StatusCode} msg={r.Error}")));

        var successCount = results.Count(r => r.Error is null);
        var conflictCount = results.Count(r => r.StatusCode == 409);

        // Exactly one redemption gets the sale; the other gets a clean 409 to retry — never
        // both succeeding (overspend) and never both failing outright.
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        await using var verifyDb = NewContext();
        var finalMembership = await verifyDb.LoyaltyMemberships.AsNoTracking().SingleAsync(m => m.Id == _membershipId);

        // Exactly one redemption landed — not StartingBalance (the successful write got lost),
        // not StartingBalance - 2*RedeemAmountPerSale (both redemptions somehow applied).
        Assert.Equal(StartingBalance - RedeemAmountPerSale, finalMembership.Balance);

        // The successful sale's own response already reflects the same persisted balance —
        // proves CreateSaleAsync isn't just returning a stale/optimistic figure to the caller.
        var successfulSale = results.Single(r => r.Error is null).Sale!;
        Assert.Equal(finalMembership.Balance, successfulSale.LoyaltyBalance);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // KI-035: this used to build (and never dispose) a brand-new NpgsqlDataSource on EVERY call —
    // each one stranding a physical Postgres backend for the rest of the run. Now one shared,
    // process-wide pool (see TestPostgres). The two redemption contexts this test races against
    // each other still get two DISTINCT physical connections: a pooled connection cannot be handed
    // to a second context while the first still holds it open, so the rendezvous is unaffected.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);

    private static PosService BuildService(AppDbContext db, Func<ILoyaltyRepository, ILoyaltyRepository> wrapLoyalty) =>
        new(
            new PosRepository(db),
            new StockRepository(db),
            new ItemRepository(db),
            new DiscountRepository(db),
            new StaticFiscalServiceFactory(new NoopFiscalService()),
            wrapLoyalty(new LoyaltyRepository(db)),
            new CustomerRepository(db),
            NullLogger<PosService>.Instance);

    private sealed class StaticFiscalServiceFactory : IFiscalServiceFactory
    {
        private readonly IFiscalService _service;
        public StaticFiscalServiceFactory(IFiscalService service) => _service = service;
        public PrroConnectionConfig? EnvFallback => null;
        public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_service);
        public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
    }

    /// <summary>
    /// Delegates everything to <paramref name="Inner"/>. Wraps
    /// <see cref="ILoyaltyRepository.GetMembershipByIdAsync"/> — the read
    /// <c>PosService.CreateSaleAsync</c> uses to fetch the membership before validating/mutating
    /// its Balance — in a two-way rendezvous with the other register's instance: whichever side
    /// is NOT <paramref name="WaitBeforeRead"/> reads first, signals <paramref name="SignalSelf"/>,
    /// then blocks on <paramref name="WaitFor"/>; the <paramref name="WaitBeforeRead"/> side
    /// blocks on <paramref name="WaitFor"/> first, then reads, then signals
    /// <paramref name="SignalSelf"/> to release the other side. Net effect: BOTH registers are
    /// guaranteed to observe the same pre-write Balance before EITHER is allowed to proceed to
    /// its write — deterministic, not timing-luck. Same shape as
    /// <c>PosConcurrencySalesIntegrationTests.RendezvousStockRepository</c>.
    /// </summary>
    private sealed class RendezvousLoyaltyRepository(
        ILoyaltyRepository Inner,
        TaskCompletionSource SignalSelf,
        Task WaitFor,
        bool WaitBeforeRead = false) : ILoyaltyRepository
    {
        public async Task<LoyaltyMembership?> GetMembershipByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            if (WaitBeforeRead)
                await WaitFor;

            var result = await Inner.GetMembershipByIdAsync(id, tenantId, ct);
            SignalSelf.TrySetResult();

            if (!WaitBeforeRead)
                await WaitFor;

            return result;
        }

        public Task<LoyaltyMembership?> GetMembershipByCardNumberAsync(long cardNumber, Guid tenantId, CancellationToken ct = default) =>
            Inner.GetMembershipByCardNumberAsync(cardNumber, tenantId, ct);
        public Task<LoyaltyMembership?> GetMembershipByTenantConsumerAsync(Guid tenantId, Guid consumerAccountId, CancellationToken ct = default) =>
            Inner.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        public Task<LoyaltyMembership?> GetMembershipByCustomerIdAsync(Guid customerId, Guid tenantId, CancellationToken ct = default) =>
            Inner.GetMembershipByCustomerIdAsync(customerId, tenantId, ct);
        public Task<LoyaltyMembership?> GetMembershipByLinkedUserAsync(Guid tenantId, Guid linkedUserId, CancellationToken ct = default) =>
            Inner.GetMembershipByLinkedUserAsync(tenantId, linkedUserId, ct);
        public Task<List<LoyaltyMembership>> GetMembershipsForConsumerAsync(Guid consumerAccountId, CancellationToken ct = default) =>
            Inner.GetMembershipsForConsumerAsync(consumerAccountId, ct);
        public Task AddMembershipAsync(LoyaltyMembership membership, CancellationToken ct = default) =>
            Inner.AddMembershipAsync(membership, ct);
        public void UpdateMembership(LoyaltyMembership membership) => Inner.UpdateMembership(membership);
        public Task<bool> TryClaimTimestepAsync(Guid membershipId, Guid tenantId, long timestep, CancellationToken ct = default) =>
            Inner.TryClaimTimestepAsync(membershipId, tenantId, timestep, ct);
        public Task<(List<LoyaltyLedgerEntry> Items, int Total)> GetLedgerPagedAsync(Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default) =>
            Inner.GetLedgerPagedAsync(tenantId, membershipId, page, pageSize, ct);
        public Task<List<LoyaltyLedgerEntry>> GetLedgerEntriesForTransactionsAsync(Guid tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default) =>
            Inner.GetLedgerEntriesForTransactionsAsync(tenantId, transactionIds, ct);
        public Task AddLedgerEntryAsync(LoyaltyLedgerEntry entry, CancellationToken ct = default) =>
            Inner.AddLedgerEntryAsync(entry, ct);
        public Task<LoyaltyProgramSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
            Inner.GetSettingsAsync(tenantId, ct);
        public Task AddSettingsAsync(LoyaltyProgramSettings settings, CancellationToken ct = default) =>
            Inner.AddSettingsAsync(settings, ct);
        public void UpdateSettings(LoyaltyProgramSettings settings) => Inner.UpdateSettings(settings);
        public Task<List<LoyaltyTierDefinition>> GetTierLadderAsync(Guid tenantId, CancellationToken ct = default) =>
            Inner.GetTierLadderAsync(tenantId, ct);
        public Task AddTierAsync(LoyaltyTierDefinition tier, CancellationToken ct = default) =>
            Inner.AddTierAsync(tier, ct);
        public void UpdateTier(LoyaltyTierDefinition tier) => Inner.UpdateTier(tier);
        public void RemoveTier(LoyaltyTierDefinition tier) => Inner.RemoveTier(tier);
        public Task<(List<LoyaltyTierChangeHistory> Items, int Total)> GetTierHistoryPagedAsync(Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default) =>
            Inner.GetTierHistoryPagedAsync(tenantId, membershipId, page, pageSize, ct);
        public Task AddTierHistoryAsync(LoyaltyTierChangeHistory history, CancellationToken ct = default) =>
            Inner.AddTierHistoryAsync(history, ct);
        public Task SaveChangesAsync(CancellationToken ct = default) => Inner.SaveChangesAsync(ct);
    }
}
