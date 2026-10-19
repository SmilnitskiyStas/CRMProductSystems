using NSubstitute;
using ShelfGuard.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Pos;

// ── Fakes ──────────────────────────────────────────────────────────────────

file static class PosServiceTestsIds
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly Guid StoreId = Guid.NewGuid();
}

file sealed class FakePosRepo : IPosRepository
{
    public List<PosShift> Shifts { get; } = [];
    public List<PosTransaction> Transactions { get; } = [];
    public List<StockEvent> Events { get; } = [];
    public int SaveCount { get; private set; }

    /// <summary>TASK-356: simulates a concurrent-write conflict (another sale raced this
    /// one on the same ProductStock row) on the Nth call to SaveChangesAsync.</summary>
    public int? ThrowConcurrencyOnSaveCall { get; set; }

    public Task<PosShift?> GetOpenShiftAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.FirstOrDefault(s => s.TenantId == tenantId && s.ClosedAt is null));

    public Task<PosShift?> GetShiftByIdAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.FirstOrDefault(s => s.Id == shiftId));

    public Task<PosShift?> GetOpenShiftForRegisterAsync(Guid registerId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.FirstOrDefault(s => s.RegisterId == registerId && s.ClosedAt is null));

    public Task<int> GetNextShiftNumberAsync(Guid tenantId, Guid? registerId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.Where(s => s.TenantId == tenantId && s.RegisterId == registerId)
            .Select(s => s.ShiftNumber).DefaultIfEmpty(0).Max() + 1);

    public Task<PosTransaction?> GetTransactionByClientIdAsync(Guid tenantId, Guid clientTransactionId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.TenantId == tenantId && t.ClientTransactionId == clientTransactionId));

    public int DiscardCount { get; private set; }
    public void DiscardPendingChanges() => DiscardCount++;

    public Task AddShiftAsync(PosShift shift, CancellationToken ct = default)
    {
        Shifts.Add(shift);
        return Task.CompletedTask;
    }

    public void UpdateShift(PosShift shift) { }

    public Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

    public Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.Where(t => t.ShiftId == shiftId).ToList());

    public Task<decimal> GetCashSalesTotalForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t => t.ShiftId == shiftId && t.PaymentType == "cash")
            .Sum(t => t.TotalAmount));

    public Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default)
    {
        Transactions.Add(tx);
        return Task.CompletedTask;
    }

    public void UpdateTransaction(PosTransaction tx) { }

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default)
    {
        Events.Add(ev);
        return Task.CompletedTask;
    }

    public Task<List<PosTransaction>> GetPendingFiscalizationAsync(
        int maxRetries, DateTime createdBefore, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t =>
                t.Status == "pending_fiscalization" &&
                t.RetryCount < maxRetries &&
                t.CreatedAt < createdBefore)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        if (ThrowConcurrencyOnSaveCall == SaveCount)
            throw new ConcurrencyConflictException("simulated concurrent write conflict");
        return Task.CompletedTask;
    }
}

file static class AttributionAccess
{
    public static IReadOnlyList<PosTransaction> Transactions(object repo) => ((FakePosRepo)repo).Transactions;
    public static IReadOnlyList<StockEvent> Events(object repo) => ((FakePosRepo)repo).Events;
}

file sealed class FakeStockRepo : IStockRepository
{
    public List<ProductStock> Batches { get; } = [];

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default) =>
        Task.FromResult(Batches
            .Where(b => b.ProductId == productId && b.StoreId == storeId && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToList());

    public Task<List<ProductStock>> GetAllAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, CancellationToken ct = default) =>
        Task.FromResult(Batches
            .Where(b => (!storeId.HasValue || b.StoreId == storeId)
                     && (!productId.HasValue || b.ProductId == productId))
            .ToList());

    public void Update(ProductStock stock) { }

    // Stubs for unused methods
    public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProductStock?>(null);
    public Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<Dictionary<Guid, List<ProductStock>>> GetDeficitStocksBulkAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        Task.FromResult(productIds.ToDictionary(id => id, _ => new List<ProductStock>()));
    public Task<(List<ProductStock> Items, int Total)> GetPagedAsync(Guid[]? storeIds, string? status, Guid? zoneId, Guid? productId, string? search, string? sortBy, bool? sortDescending, int page, int pageSize, Guid? categoryId = null, decimal? minQuantity = null, decimal? maxQuantity = null, CancellationToken ct = default) =>
        Task.FromResult((new List<ProductStock>(), 0));
    public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) => Task.FromResult(new List<Location>());
    public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid[]? storeIds, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
    public Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(Guid[]? storeIds, CancellationToken ct = default) =>
        Task.FromResult(new List<(Guid?, string, string, string)>());
    public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeCatalogRepo : IItemRepository
{
    public List<Item> Products { get; } = [];

    public Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Barcodes.Contains(barcode)));

    public Task<IReadOnlyList<Item>> GetByAnyBarcodeAsync(IReadOnlyList<string> barcodes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Item>>(Products.Where(p => p.Barcodes.Any(barcodes.Contains)).ToList());

    public Task<List<Item>> GetAllAsync(Guid? categoryId, Guid? segmentId, string? managementType, bool? uncategorized = null, CancellationToken ct = default) =>
        Task.FromResult(Products);

    public Task<(List<Item> Items, int Total)> GetPagedAsync(Guid? categoryId, Guid? segmentId, string? managementType, string? search, IReadOnlyList<Guid>? ids, string? sortBy, bool? sortDescending, int page, int pageSize, decimal? minPrice = null, decimal? maxPrice = null, bool? uncategorized = null, CancellationToken ct = default) =>
        Task.FromResult((Products, Products.Count));

    public Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<Item?> GetForBarcodeMergeAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyDictionary<Guid, ItemPromoInfo>> GetPromoStatesAsync(IReadOnlyList<Guid> productIds, int upcomingWithinDays, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ItemPromoInfo>>(new Dictionary<Guid, ItemPromoInfo>());

    public Task<IReadOnlyDictionary<Guid, ItemBufferSuggestion>> GetBufferSuggestionsAsync(IReadOnlyList<Guid> productIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ItemBufferSuggestion>>(new Dictionary<Guid, ItemBufferSuggestion>());

    public Task<ItemPromoDetail?> GetPromoDetailAsync(Guid productId, int upcomingWithinDays, CancellationToken ct = default) =>
        Task.FromResult<ItemPromoDetail?>(null);

    public Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default) => Task.FromResult(new List<ProductSupplierSetting>());
    public Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default) => Task.FromResult(false);

    // TASK-717: zone names for the catalog "Zones" column — unused by POS tests.
    public Task<Dictionary<Guid, List<string>>> GetZoneNamesAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<Guid, List<string>>());

    // TASK-714: product ↔ store-zone tags — unused by POS tests.
    public Task<List<ItemZoneAssignment>> GetZoneAssignmentsAsync(Guid itemId, CancellationToken ct = default) => Task.FromResult(new List<ItemZoneAssignment>());
    public Task<bool> ZoneAssignmentExistsAsync(Guid itemId, Guid zoneId, CancellationToken ct = default) => Task.FromResult(false);
    public Task<ItemZoneAssignment?> GetZoneAssignmentAsync(Guid itemId, Guid zoneId, CancellationToken ct = default) => Task.FromResult<ItemZoneAssignment?>(null);

    public Task AddAsync(Item product, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddZoneAssignmentAsync(ItemZoneAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
    public void RemoveZoneAssignment(ItemZoneAssignment assignment) { }
    public void Update(Item product) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeDiscountRepo : IDiscountRepository
{
    public List<Discount> Discounts { get; } = [];

    public Task<IReadOnlyList<Discount>> GetAllAsync(Guid tenantId, Guid? storeId = null, string? status = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Discount>>(Discounts
            .Where(d => d.TenantId == tenantId
                && (!storeId.HasValue || d.StoreId == storeId)
                && (status is null || d.Status == status))
            .ToList());

    public Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Discounts.FirstOrDefault(d => d.Id == id));

    public Task AddAsync(Discount discount, CancellationToken ct = default) => Task.CompletedTask;
    public void Update(Discount discount) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>TASK-405 (Loyalty Фаза 0): fake ILoyaltyRepository backing PosService's new accrual/redemption path.</summary>
file sealed class FakeLoyaltyRepo : ILoyaltyRepository
{
    public List<LoyaltyMembership> Memberships { get; } = [];
    public List<LoyaltyLedgerEntry> LedgerEntries { get; } = [];
    public List<LoyaltyProgramSettings> Settings { get; } = [];

    public Task<LoyaltyMembership?> GetMembershipByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id && m.TenantId == tenantId));

    public Task<LoyaltyMembership?> GetMembershipByCardNumberAsync(long cardNumber, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.CardNumber == cardNumber && m.TenantId == tenantId));

    public Task<LoyaltyMembership?> GetMembershipByTenantConsumerAsync(Guid tenantId, Guid consumerAccountId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.ConsumerAccountId == consumerAccountId));

    public Task<LoyaltyMembership?> GetMembershipByCustomerIdAsync(Guid customerId, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.CustomerId == customerId && m.TenantId == tenantId));

    public Task<LoyaltyMembership?> GetMembershipByLinkedUserAsync(Guid tenantId, Guid linkedUserId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.LinkedUserId == linkedUserId));

    public Task<List<LoyaltyMembership>> GetMembershipsForConsumerAsync(Guid consumerAccountId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.Where(m => m.ConsumerAccountId == consumerAccountId).ToList());

    public Task AddMembershipAsync(LoyaltyMembership membership, CancellationToken ct = default)
    {
        Memberships.Add(membership);
        return Task.CompletedTask;
    }

    public void UpdateMembership(LoyaltyMembership membership) { }

    public Task<bool> TryClaimTimestepAsync(Guid membershipId, Guid tenantId, long timestep, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<(List<LoyaltyLedgerEntry> Items, int Total)> GetLedgerPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default)
    {
        var items = LedgerEntries.Where(e => e.TenantId == tenantId && e.MembershipId == membershipId).ToList();
        return Task.FromResult((items, items.Count));
    }

    public Task<List<LoyaltyLedgerEntry>> GetLedgerEntriesForTransactionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default) =>
        Task.FromResult(LedgerEntries
            .Where(e => e.TenantId == tenantId && e.PosTransactionId.HasValue && transactionIds.Contains(e.PosTransactionId.Value))
            .ToList());

    public Task AddLedgerEntryAsync(LoyaltyLedgerEntry entry, CancellationToken ct = default)
    {
        LedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<LoyaltyProgramSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Settings.FirstOrDefault(s => s.TenantId == tenantId));

    public Task AddSettingsAsync(LoyaltyProgramSettings settings, CancellationToken ct = default)
    {
        Settings.Add(settings);
        return Task.CompletedTask;
    }

    public void UpdateSettings(LoyaltyProgramSettings settings) { }

    // Stubs for unused methods (TASK-615 tier ladder — not exercised by PosService tests,
    // which set LoyaltyMembership.CurrentTier directly since fakes don't run EF Include).
    public Task<List<LoyaltyTierDefinition>> GetTierLadderAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(new List<LoyaltyTierDefinition>());
    public Task AddTierAsync(LoyaltyTierDefinition tier, CancellationToken ct = default) => Task.CompletedTask;
    public void UpdateTier(LoyaltyTierDefinition tier) { }
    public void RemoveTier(LoyaltyTierDefinition tier) { }
    public Task<(List<LoyaltyTierChangeHistory> Items, int Total)> GetTierHistoryPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult((new List<LoyaltyTierChangeHistory>(), 0));

    public Task AddTierHistoryAsync(LoyaltyTierChangeHistory history, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>TASK-405: fake ICustomerRepository backing PosService's Customer.TotalOrders/TotalSpent update.</summary>
file sealed class FakeCustomerRepo : ICustomerRepository
{
    public Task<CustomerActivityStatsRow> GetActivityStatsAsync(Guid customerId, Guid tenantId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(new CustomerActivityStatsRow(0, 0m, null, null, 0, 0m, 0, 0m, 0m));
    public Task<IReadOnlyList<CustomerStoreActivityRow>> GetStoreActivityAsync(Guid customerId, Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerStoreActivityRow>>([]);
    public Task<IReadOnlyList<CustomerMonthActivityRow>> GetMonthlyActivityAsync(Guid customerId, Guid tenantId, DateTime fromUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerMonthActivityRow>>([]);
    public Task<IReadOnlyList<CustomerTopProductRow>> GetTopProductsAsync(Guid customerId, Guid tenantId, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerTopProductRow>>([]);
    public Task<(IReadOnlyList<CustomerReceiptRow> Items, int Total)> GetReceiptsPagedAsync(Guid customerId, Guid tenantId, int page, int pageSize, CancellationToken ct) => Task.FromResult<(IReadOnlyList<CustomerReceiptRow>, int)>(([], 0));
    public List<Customer> Customers { get; } = [];

    public Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.Where(c => c.TenantId == tenantId).ToList());

    public Task<(List<Customer> Items, int Total)> GetPagedAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var items = Customers.Where(c => c.TenantId == tenantId).ToList();
        return Task.FromResult((items, items.Count));
    }

    public Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id && c.TenantId == tenantId));

    public Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id && c.TenantId == tenantId));

    public Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct) =>
        Task.FromResult(Customers.Any(c => c.TenantId == tenantId && c.Phone == phone && c.Id != excludeId));

    public Task<Customer?> FindByPhoneAsync(string phone, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.TenantId == tenantId && c.Phone == phone));

    public Task<Customer> CreateAsync(Customer customer, CancellationToken ct)
    {
        Customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task UpdateAsync(Customer customer, CancellationToken ct) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        Customers.RemoveAll(c => c.Id == id && c.TenantId == tenantId);
        return Task.CompletedTask;
    }
}

file sealed class FakeFiscalFactory : IFiscalServiceFactory
{
    private readonly IFiscalService _service;
    public PrroConnectionConfig? EnvFallback => null;
    public FakeFiscalFactory(IFiscalService service) => _service = service;
    public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(_service);
    public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
}

/// <summary>Simulates a Checkbox provider that fiscalizes instantly and successfully —
/// used to pin the TASK-356 inline (awaited) online-fiscalization behavior.</summary>
file sealed class FakeSuccessfulFiscalService : IFiscalService
{
    public Task<FiscalHealthResult> PingAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalHealthResult(true, "fake", "FN-TEST", true, false, null));

    public Task<FiscalCashierResult> CheckCashierAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalCashierResult(true, "Test Cashier", null));

    public Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult("shift-1", FiscalShiftStatus.Opened, 1, DateTimeOffset.UtcNow, null));

    public Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult("shift-1", FiscalShiftStatus.Closed, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    public Task<FiscalShiftResult> GetShiftStatusAsync(string providerShiftId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(providerShiftId, FiscalShiftStatus.Opened, 1, DateTimeOffset.UtcNow, null));

    public Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            request.LocalReceiptId, FiscalReceiptStatus.Done, "FN-12345", DateTimeOffset.UtcNow, null, null));

    public Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            providerReceiptId, FiscalReceiptStatus.Done, "FN-12345", DateTimeOffset.UtcNow, null, null));
}

file sealed record Seeded(FakePosRepo Pos, FakeStockRepo Stock, FakeCatalogRepo Catalog, PosShift Shift, ProductStock Batch)
{
    public void Deconstruct(out FakePosRepo pos, out FakeStockRepo stock, out FakeCatalogRepo catalog, out PosShift shift, out ProductStock batch)
    {
        pos = Pos; stock = Stock; catalog = Catalog; shift = Shift; batch = Batch;
    }
}

file static class SaleSeeder
{
    public static Seeded Seed(string barcode, decimal stockQty = 10m, Guid? registerId = null)
    {
        var tenantId = PosServiceTestsIds.TenantId;
        var storeId = PosServiceTestsIds.StoreId;
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = tenantId, StoreId = storeId, RegisterId = registerId };
        pos.Shifts.Add(shift);
        var product = new Item { Id = Guid.NewGuid(), TenantId = tenantId, Barcodes = [barcode], Name = "Seed", PriceRetail = 10m };
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);
        var stock = new FakeStockRepo();
        var batch = new ProductStock
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, StoreId = storeId,
            Quantity = stockQty, QuantityInitial = stockQty,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Status = "safe",
        };
        stock.Batches.Add(batch);
        return new Seeded(pos, stock, catalog, shift, batch);
    }
}

// ── Tests ──────────────────────────────────────────────────────────────────

public sealed class PosServiceTests
{
    private static readonly Guid TenantId = PosServiceTestsIds.TenantId;
    private static readonly Guid CashierId = Guid.NewGuid();
    private static readonly Guid StoreId = PosServiceTestsIds.StoreId;

    private static PosService BuildService(
        IPosRepository? pos = null,
        IStockRepository? stock = null,
        IItemRepository? catalog = null,
        IDiscountRepository? discounts = null,
        IFiscalService? fiscal = null,
        ILoyaltyRepository? loyalty = null,
        ICustomerRepository? customers = null,
        IUserRepository? users = null,
        ICashRegisterRepository? registers = null)
    {
        return new PosService(
            pos ?? new FakePosRepo(),
            stock ?? new FakeStockRepo(),
            catalog ?? new FakeCatalogRepo(),
            discounts ?? new FakeDiscountRepo(),
            new FakeFiscalFactory(fiscal ?? new NoopFiscalService()),
            loyalty ?? new FakeLoyaltyRepo(),
            customers ?? new FakeCustomerRepo(),
            NullLogger<PosService>.Instance,
            users,
            registers);
    }

    private static Item MakeProduct(string barcode = "123", decimal price = 10m) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Barcodes = [barcode],
            Name = "Тестовий товар",
            PriceRetail = price,
        };

    private static ProductStock MakeBatch(Guid productId, decimal qty, int daysUntilExpiry = 30) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = productId,
            StoreId = StoreId,
            Quantity = qty,
            QuantityInitial = qty,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            Status = "safe",
        };

    // ── Open Shift ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenShift_creates_shift_in_repo()
    {
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId, 500m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(StoreId, shift.StoreId);
        Assert.Single(pos.Shifts);
    }

    [Fact]
    public async Task OpenShift_returns_409_when_shift_already_open()
    {
        var pos = new FakePosRepo();
        pos.Shifts.Add(new PosShift { TenantId = TenantId, StoreId = StoreId });
        var svc = BuildService(pos: pos);

        var (shift, error, statusCode) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId));

        Assert.Null(shift);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task OpenShift_fiscal_failure_still_returns_shift()
    {
        // Noop provider returns PendingFiscalization — shift still persisted
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos, fiscal: new NoopFiscalService());

        var (shift, error, _) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId));

        Assert.Null(error);
        Assert.NotNull(shift);
        // fiscal status with noop → local_only (no provider shift id)
        Assert.Equal("local_only", shift.FiscalStatus);
    }

    // ── Close Shift ────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseShift_returns_404_when_no_open_shift()
    {
        var svc = BuildService();

        var (_, error, statusCode) = await svc.CloseShiftAsync(TenantId);

        Assert.Equal(404, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CloseShift_sets_ClosedAt()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.CloseShiftAsync(TenantId);

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.NotNull(existing.ClosedAt);
    }

    [Fact]
    public async Task CloseShift_without_cash_count_leaves_reconciliation_null()
    {
        // Backward compat: omitting the body (or ActualClosingCash) closes exactly as
        // before TASK-356 — no reconciliation performed.
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, request: null);

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Null(shift.ClosingCash);
        Assert.Null(shift.ExpectedCashAmount);
        Assert.Null(shift.CashDiscrepancy);
    }

    // ── Close Shift — cash reconciliation (TASK-356) ─────────────────────────

    [Fact]
    public async Task CloseShift_cash_count_matches_expected_zero_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        // Cash sale 300 + card sale 700 — card must NOT count toward expected cash.
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "card", TotalAmount = 700m, ReceiptNumber = "R-2" });
        var svc = BuildService(pos: pos);

        // Expected = OpeningCash(500) + cash sales(300) = 800
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(800m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(800m, shift.ClosingCash);
        Assert.Equal(0m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_cash_count_shortage_returns_negative_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        var svc = BuildService(pos: pos);

        // Expected = 500 + 300 = 800; cashier counted only 750 — 50 missing.
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(750m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(750m, shift.ClosingCash);
        Assert.Equal(-50m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_cash_count_surplus_returns_positive_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        var svc = BuildService(pos: pos);

        // Expected = 500 + 300 = 800; cashier counted 820 — 20 extra.
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(820m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(20m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_negative_cash_count_returns_400()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, statusCode) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(-1m));

        Assert.Null(shift);
        Assert.Equal(400, statusCode);
        Assert.NotNull(error);
        // Shift must stay open — validation failed before anything was persisted.
        Assert.Null(existing.ClosedAt);
    }

    [Fact]
    public async Task CloseShift_already_closed_shift_returns_404_on_second_attempt()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (firstShift, firstError, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(0m));
        Assert.Null(firstError);
        Assert.NotNull(firstShift);

        var (secondShift, secondError, statusCode) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(0m));

        Assert.Null(secondShift);
        Assert.Equal(404, statusCode);
        Assert.NotNull(secondError);
    }

    // ── Get Current Shift ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentShift_returns_null_when_no_open_shift()
    {
        var svc = BuildService();
        var result = await svc.GetCurrentShiftAsync(TenantId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentShift_returns_open_shift()
    {
        var pos = new FakePosRepo();
        pos.Shifts.Add(new PosShift { TenantId = TenantId, StoreId = StoreId });
        var svc = BuildService(pos: pos);

        var result = await svc.GetCurrentShiftAsync(TenantId);

        Assert.NotNull(result);
        Assert.Equal(StoreId, result.StoreId);
    }

    // ── Create Sale ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSale_returns_409_when_shift_not_found()
    {
        var svc = BuildService();

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(
                ShiftId: Guid.NewGuid(),
                Items: [new SaleItemRequest("123", 1)],
                PaymentType: "Cash",
                PaymentAmount: 10));

        Assert.Equal(409, statusCode);
        Assert.Null(sale);
    }

    [Fact]
    public async Task CreateSale_returns_409_when_shift_belongs_to_different_tenant()
    {
        var pos = new FakePosRepo();
        var otherTenantShift = new PosShift
        {
            TenantId = Guid.NewGuid(), // different tenant
            StoreId = StoreId,
        };
        pos.Shifts.Add(otherTenantShift);
        var svc = BuildService(pos: pos);

        var (_, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(otherTenantShift.Id, [new SaleItemRequest("123", 1)], "Cash", 10));

        Assert.Equal(409, statusCode);
    }

    [Fact]
    public async Task CreateSale_returns_400_when_barcode_not_found()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var catalog = new FakeCatalogRepo(); // empty
        var svc = BuildService(pos: pos, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("UNKNOWN-BAR", 1)], "Cash", 10));

        Assert.Equal(400, statusCode);
        Assert.Contains("UNKNOWN-BAR", error);
    }

    [Fact]
    public async Task CreateSale_returns_423_when_all_batches_expired()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("EXPIRED_BAR");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // All batches expired (daysUntilExpiry = -1)
        stock.Batches.Add(new ProductStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = product.Id,
            StoreId = StoreId,
            Quantity = 5,
            QuantityInitial = 5,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = "expired",
        });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("EXPIRED_BAR", 1)], "Cash", 10));

        Assert.Equal(423, statusCode);
        Assert.NotNull(error);
        Assert.Contains("expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_returns_400_when_insufficient_stock()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("LOW_STOCK");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 2)); // only 2 available

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("LOW_STOCK", 5)], "Cash", 50));

        Assert.Equal(400, statusCode);
        Assert.Contains("Insufficient stock", error);
    }

    [Fact]
    public async Task CreateSale_applies_fefo_order()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FEFO_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // Batch A: expires in 30 days, qty 3
        var batchA = MakeBatch(product.Id, qty: 3, daysUntilExpiry: 30);
        // Batch B: expires in 5 days (nearer!), qty 10 — FEFO should consume this first
        var batchB = MakeBatch(product.Id, qty: 10, daysUntilExpiry: 5);
        stock.Batches.Add(batchA);
        stock.Batches.Add(batchB);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FEFO_TEST", 2)], "Cash", 20));

        Assert.Null(error);
        Assert.NotNull(sale);

        // Batch B (nearest expiry) should be consumed first → its qty drops by 2
        Assert.Equal(8, batchB.Quantity); // consumed 2 from batchB
        Assert.Equal(3, batchA.Quantity); // batchA untouched
    }

    [Fact]
    public async Task CreateSale_fefo_spans_multiple_batches()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("MULTI_BATCH");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        var batchNear = MakeBatch(product.Id, qty: 3, daysUntilExpiry: 5);  // consumed first
        var batchFar = MakeBatch(product.Id, qty: 5, daysUntilExpiry: 30);  // remainder from here
        stock.Batches.Add(batchNear);
        stock.Batches.Add(batchFar);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("MULTI_BATCH", 5)], "Cash", 50));

        Assert.Null(error);
        // 3 consumed from near batch (now 0), 2 from far batch (now 3)
        Assert.Equal(0, batchNear.Quantity);
        Assert.Equal(3, batchFar.Quantity);
    }

    [Fact]
    public async Task CreateSale_calculates_totals_correctly()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("TOTALS_TEST", price: 25m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("TOTALS_TEST", 3)], "Cash", 100m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(75m, sale.Subtotal);       // 3 × 25
        Assert.Equal(25m, sale.Change);          // 100 - 75
    }

    [Fact]
    public async Task CreateSale_creates_stock_events_type_pos_sale()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("EVENT_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("EVENT_TEST", 2)], "Cash", 20));

        Assert.Contains(pos.Events, e => e.EventType == "pos_sale");
        Assert.All(pos.Events, e => Assert.Equal("pos_sale", e.EventType));
    }

    [Fact]
    public async Task CreateSale_fiscal_failure_does_not_block_sale()
    {
        // Noop = no fiscal provider → sale still committed
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FISCAL_FAIL");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, fiscal: new NoopFiscalService());

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FISCAL_FAIL", 1)], "Cash", 10));

        Assert.Null(error);
        Assert.NotNull(sale);
        // Status starts as pending_fiscalization (fiscal runs async in background)
        Assert.Equal("pending_fiscalization", sale.FiscalStatus);
        Assert.Null(sale.FiscalNumber);
    }

    /// <summary>
    /// TASK-356: before this fix, online fiscalization ran on a detached, un-awaited
    /// Task.Run — the response was always built and returned before that background task
    /// could possibly complete, so a successful fiscal outcome could never be reflected in
    /// the SaleDto (untestable with a synchronous unit test, by construction). Now that the
    /// attempt is inline and awaited, a fast/successful provider response is visible
    /// directly in the returned SaleDto — this is the regression guard for that fix.
    /// </summary>
    [Fact]
    public async Task CreateSale_successful_online_fiscalization_is_reflected_in_response()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FISCAL_OK");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, fiscal: new FakeSuccessfulFiscalService());

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FISCAL_OK", 1)], "Cash", 10));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal("fiscalized", sale.FiscalStatus);
        Assert.Equal("FN-12345", sale.FiscalNumber);
    }

    /// <summary>
    /// TASK-356: ProductStock now carries an xmin optimistic-concurrency token so two
    /// sales racing on the same batch can't silently oversell (see the real-Postgres
    /// PosConcurrencySalesIntegrationTests for the end-to-end proof). This test pins the
    /// service-layer half of that fix in isolation: PosService must translate a
    /// ConcurrencyConflictException raised from the final SaveChangesAsync into a clean
    /// 409 instead of letting it propagate as an unhandled exception (which would surface
    /// as a raw 500 to the cashier with no actionable message).
    /// </summary>
    [Fact]
    public async Task CreateSale_concurrency_conflict_on_commit_returns_409()
    {
        var pos = new FakePosRepo { ThrowConcurrencyOnSaveCall = 1 };
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CONFLICT_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 1));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CONFLICT_TEST", 1)], "Cash", 10));

        Assert.Null(sale);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
        // Only the one (conflicting) SaveChangesAsync call happened — the method returned
        // immediately on the conflict instead of proceeding to attempt fiscalization.
        Assert.Equal(1, pos.SaveCount);
    }

    [Fact]
    public async Task CreateSale_returns_400_on_invalid_payment_type()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Bitcoin", 10));

        Assert.Equal(400, statusCode);
        Assert.Contains("PaymentType", error);
    }

    [Fact]
    public async Task CreateSale_applies_auto_discount_for_critical_product()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CRITICAL_PROD", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // Batch is critical (expires in 4 days) — within CriticalDays=6 threshold
        var critBatch = new ProductStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = product.Id,
            StoreId = StoreId,
            Quantity = 10,
            QuantityInitial = 10,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
            Status = "critical",
        };
        stock.Batches.Add(critBatch);

        // Auto-discount of 20%: discounted price = 80
        var discount = Discount.Create(
            tenantId: TenantId,
            productId: product.Id,
            storeId: StoreId,
            discountPercent: 20m,
            reason: "expiry",
            priceOriginal: 100m,
            autoApplied: true);
        discount.Approve(CashierId);

        var discounts = new FakeDiscountRepo();
        discounts.Discounts.Add(discount);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, discounts: discounts);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CRITICAL_PROD", 1)], "Cash", 100));

        Assert.Null(error);
        Assert.NotNull(sale);

        var item = Assert.Single(sale.Items);
        Assert.Equal(20m, item.DiscountAmount);     // 20% of 100
        Assert.Equal(80m, item.Total);              // 100 - 20
    }

    // ── Create Sale — Loyalty (TASK-405) ──────────────────────────────────

    [Fact]
    public async Task CreateSale_without_loyalty_fields_behaves_exactly_as_before()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("NO_LOYALTY", price: 20m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("NO_LOYALTY", 1)], "Cash", 20m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(20m, sale.Subtotal);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Null(sale.LoyaltyBalance);
    }

    [Fact]
    public async Task CreateSale_with_customerId_updates_customer_aggregates()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CUST_AGG", price: 50m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var customer = new Customer { TenantId = TenantId, Name = "Ірина", TotalOrders = 2, TotalSpent = 100m };
        var customers = new FakeCustomerRepo();
        customers.Customers.Add(customer);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, customers: customers);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CUST_AGG", 1)], "Cash", 50m, CustomerId: customer.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(3, customer.TotalOrders);
        Assert.Equal(150m, customer.TotalSpent);
    }

    [Fact]
    public async Task CreateSale_with_unknown_customerId_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, CustomerId: Guid.NewGuid()));

        Assert.Equal(400, statusCode);
        Assert.Contains("Customer", error);
    }

    [Fact]
    public async Task CreateSale_with_loyalty_membership_accrues_bonus()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("ACCRUAL", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("ACCRUAL", 1)], "Cash", 100m, LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(10m, sale.LoyaltyAccrued);
        Assert.Equal(10m, membership.Balance);
        Assert.Equal(10m, sale.LoyaltyBalance);
        Assert.Single(loyalty.LedgerEntries);
        Assert.Equal(LoyaltyEntryType.Accrual, loyalty.LedgerEntries[0].EntryType);
    }

    [Fact]
    public async Task CreateSale_with_redemption_reduces_total_and_records_ledger()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("REDEEM", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 50m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings
        {
            TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 0m, RedemptionCapPercent = 50m,
        });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("REDEEM", 1)], "Cash", 80m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 20m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(80m, sale.Subtotal);       // 100 - 20 redeemed
        Assert.Equal(20m, sale.LoyaltyRedeemed);
        Assert.Equal(30m, membership.Balance);  // 50 - 20
        Assert.Single(loyalty.LedgerEntries);
        Assert.Equal(LoyaltyEntryType.Redemption, loyalty.LedgerEntries[0].EntryType);
        Assert.Equal(-20m, loyalty.LedgerEntries[0].Amount);
    }

    [Fact]
    public async Task CreateSale_applies_bonus_units_conversion_to_accrual_and_redemption()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var product = MakeProduct("CONVERT_POINTS", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);
        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));
        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 1_000m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings
        {
            TenantId = TenantId,
            IsEnabled = true,
            AccrualRatePercent = 10m,
            RedemptionCapPercent = 100m,
            BonusUnitsPerCurrencyUnit = 100,
        });
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CONVERT_POINTS", 1)], "Cash", 99m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 100m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(99m, sale.Subtotal);              // 100 bonusів = 1 грн знижки
        Assert.Equal(100m, sale.LoyaltyRedeemed);     // API та баланс залишаються в бонусних одиницях
        Assert.Equal(990m, sale.LoyaltyAccrued);      // 10% від 99 грн × 100 бонусів/грн
        Assert.Equal(1_890m, membership.Balance);
    }

    [Fact]
    public async Task CreateSale_redemption_over_cap_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("OVERCAP", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 100m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, RedemptionCapPercent = 50m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        // Cap is 50% of 100 = 50; trying to redeem 60 must fail.
        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("OVERCAP", 1)], "Cash", 40m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 60m));

        Assert.Equal(400, statusCode);
        Assert.Contains("cap", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100m, membership.Balance); // unchanged
        Assert.Empty(loyalty.LedgerEntries);
    }

    [Fact]
    public async Task CreateSale_redemption_over_balance_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("OVERBALANCE", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 5m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, RedemptionCapPercent = 100m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("OVERBALANCE", 1)], "Cash", 90m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 10m));

        Assert.Equal(400, statusCode);
        Assert.Contains("Insufficient loyalty balance", error);
    }

    [Fact]
    public async Task CreateSale_blocked_membership_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var membership = new LoyaltyMembership { TenantId = TenantId, Status = LoyaltyMembershipStatus.Blocked };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);

        var svc = BuildService(pos: pos, loyalty: loyalty);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, LoyaltyMembershipId: membership.Id));

        Assert.Equal(400, statusCode);
        Assert.Contains("blocked", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_redeemAmount_without_membershipId_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, RedeemAmount: 5m));

        Assert.Equal(400, statusCode);
        Assert.Contains("LoyaltyMembershipId", error);
    }

    [Fact]
    public async Task CreateSale_loyalty_disabled_by_settings_skips_accrual_but_still_sells()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("DISABLED_PROGRAM", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = false, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("DISABLED_PROGRAM", 1)], "Cash", 100m,
                LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Equal(0m, membership.Balance);
        Assert.Empty(loyalty.LedgerEntries);
    }

    // ── Create Sale — Loyalty tier ladder (TASK-615) ──────────────────────

    /// <summary>Regression pin: a membership with no CurrentTier assigned yet (nightly
    /// recompute hasn't run) must behave exactly as before tier support existed — accrual
    /// multiplier 1.0, discount 0%.</summary>
    [Fact]
    public async Task CreateSale_membership_without_tier_behaves_exactly_as_before()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("NO_TIER", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        // CurrentTierId/CurrentTier both left unset — same as every membership before TASK-613.
        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("NO_TIER", 1)], "Cash", 100m, LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(100m, sale.Subtotal);   // no tier discount
        Assert.Equal(10m, sale.LoyaltyAccrued); // 10% of 100, multiplier 1.0
        Assert.Equal(10m, membership.Balance);
        var item = Assert.Single(sale.Items);
        Assert.Equal(0m, item.DiscountAmount);
    }

    [Fact]
    public async Task CreateSale_uses_current_tier_cashback_percent()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("TIER_MULTIPLIER", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var tier = new LoyaltyTierDefinition
        {
            TenantId = TenantId, Name = "Gold", SortOrder = 1, AccrualMultiplier = 15m, DiscountPercent = 0m,
        };
        var membership = new LoyaltyMembership
        {
            TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active,
            CurrentTierId = tier.Id, CurrentTier = tier,
        };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("TIER_MULTIPLIER", 1)], "Cash", 100m, LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        // The tier owns its cashback directly: 15% of 100 = 15.
        Assert.Equal(15m, sale.LoyaltyAccrued);
        Assert.Equal(15m, membership.Balance);
    }

    [Fact]
    public async Task CreateSale_tier_discount_reduces_item_total_and_accrual_base()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("TIER_DISCOUNT", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var tier = new LoyaltyTierDefinition
        {
            TenantId = TenantId, Name = "Platinum", SortOrder = 1, AccrualMultiplier = 10m, DiscountPercent = 10m,
        };
        var membership = new LoyaltyMembership
        {
            TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active,
            CurrentTierId = tier.Id, CurrentTier = tier,
        };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("TIER_DISCOUNT", 1)], "Cash", 90m, LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);

        var item = Assert.Single(sale.Items);
        Assert.Equal(10m, item.DiscountAmount);  // 10% of 100
        Assert.Equal(90m, item.Total);
        Assert.Equal(90m, sale.Subtotal);        // tx.TotalAmount already net of the tier discount

        // Accrual base is the discounted total: 10% of 90 = 9.
        Assert.Equal(9m, sale.LoyaltyAccrued);
        Assert.Equal(9m, membership.Balance);
    }

    // ── List Sales ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSalesForShift_returns_empty_list_when_no_transactions()
    {
        var svc = BuildService();
        var result = await svc.GetSalesForShiftAsync(TenantId, Guid.NewGuid());
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalAmount);
    }

    // ── List Sales — Customer + Loyalty mapping (TASK-410) ─────────────────

    [Fact]
    public async Task GetSalesForShift_maps_customer_and_leaves_loyalty_null_with_no_ledger_activity()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var customer = new Customer { TenantId = TenantId, Name = "Ірина" };
        var tx = new PosTransaction
        {
            TenantId = TenantId,
            ShiftId = shiftId,
            PaymentType = "cash",
            TotalAmount = 100m,
            ReceiptNumber = "R-1",
            CustomerId = customer.Id,
            Customer = customer, // FakePosRepo doesn't run EF Include — set directly.
        };
        pos.Transactions.Add(tx);

        var svc = BuildService(pos: pos);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal("Ірина", sale.CustomerName);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Null(sale.LoyaltyBalance);
    }

    [Fact]
    public async Task GetSalesForShift_maps_accrual_only_ledger_entry()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var tx = new PosTransaction
        {
            TenantId = TenantId, ShiftId = shiftId, PaymentType = "cash",
            TotalAmount = 100m, ReceiptNumber = "R-1",
        };
        pos.Transactions.Add(tx);

        var loyalty = new FakeLoyaltyRepo();
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = Guid.NewGuid(),
            EntryType = LoyaltyEntryType.Accrual,
            Amount = 10m,
            BalanceAfter = 40m,
            PosTransactionId = tx.Id,
        });

        var svc = BuildService(pos: pos, loyalty: loyalty);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(10m, sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Equal(40m, sale.LoyaltyBalance);
    }

    [Fact]
    public async Task GetSalesForShift_balance_reflects_last_ledger_entry_when_both_redemption_and_accrual_exist()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var tx = new PosTransaction
        {
            TenantId = TenantId, ShiftId = shiftId, PaymentType = "cash",
            TotalAmount = 80m, ReceiptNumber = "R-1",
        };
        pos.Transactions.Add(tx);

        // Mirrors CreateSaleAsync's write order: redemption entry persisted first, accrual
        // second — explicit CreatedAt values (rather than relying on two back-to-back
        // DateTimeOffset.UtcNow calls) so the "last entry wins" ordering is deterministic.
        var membershipId = Guid.NewGuid();
        var loyalty = new FakeLoyaltyRepo();
        var now = DateTimeOffset.UtcNow;
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = membershipId,
            EntryType = LoyaltyEntryType.Redemption,
            Amount = -20m,
            BalanceAfter = 30m,
            PosTransactionId = tx.Id,
            CreatedAt = now,
        });
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = membershipId,
            EntryType = LoyaltyEntryType.Accrual,
            Amount = 8m,
            BalanceAfter = 38m,
            PosTransactionId = tx.Id,
            CreatedAt = now.AddMilliseconds(1),
        });

        var svc = BuildService(pos: pos, loyalty: loyalty);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(8m, sale.LoyaltyAccrued);
        Assert.Equal(20m, sale.LoyaltyRedeemed); // positive, sign flipped from the stored -20m
        Assert.Equal(38m, sale.LoyaltyBalance);  // last entry (accrual) wins, not redemption's 30m
    }
    // ── Cashier app: register binding, idempotency, batch sync ─────────────

    [Fact]
    public async Task CreateSale_same_ClientTransactionId_twice_creates_one_transaction()
    {
        var (pos, stock, catalog, shift, batch) = SaleSeeder.Seed("IDEMP-1");
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var clientId = Guid.NewGuid();
        var request = new CreateSaleRequest(
            shift.Id, [new SaleItemRequest("IDEMP-1", 2)], "Cash", 20, ClientTransactionId: clientId);

        var (first, err1, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);
        var (second, err2, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);

        Assert.Null(err1);
        Assert.Null(err2);
        Assert.Single(pos.Transactions);
        Assert.Equal(first!.TransactionId, second!.TransactionId);
        Assert.Equal(first.ReceiptNumber, second.ReceiptNumber);
        Assert.Equal(8m, batch.Quantity); // FEFO write-down happened exactly once
        Assert.Equal(clientId, pos.Transactions[0].ClientTransactionId);
    }

    [Fact]
    public async Task CreateSale_replay_returns_same_tendered_amount_and_change()
    {
        var (pos, stock, catalog, shift, _) = SaleSeeder.Seed("IDEMP-CHG");
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var request = new CreateSaleRequest(
            shift.Id, [new SaleItemRequest("IDEMP-CHG", 1)], "Cash", 100, ClientTransactionId: Guid.NewGuid());

        var (first, _, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);
        var (second, _, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);

        Assert.True(first!.Change > 0);
        Assert.Equal(100m, second!.PaymentAmount);
        Assert.Equal(first.Change, second.Change);
    }

    [Fact]
    public async Task OpenShift_assigns_local_sequential_number_per_register()
    {
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos);
        var regA = Guid.NewGuid();

        var (a, _, _) = await svc.OpenShiftAsync(TenantId, CashierId, new OpenShiftRequest(StoreId), registerId: regA);
        await svc.CloseShiftAsync(TenantId, registerId: regA);
        var (a2, _, _) = await svc.OpenShiftAsync(TenantId, CashierId, new OpenShiftRequest(StoreId), registerId: regA);

        Assert.Equal(1, a!.ShiftNumber);
        Assert.Equal(2, a2!.ShiftNumber);
    }

    [Fact]
    public async Task CreateSale_without_ClientTransactionId_is_not_deduplicated()
    {
        var (pos, stock, catalog, shift, _) = SaleSeeder.Seed("IDEMP-2");
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var request = new CreateSaleRequest(shift.Id, [new SaleItemRequest("IDEMP-2", 1)], "Cash", 10);

        await svc.CreateSaleAsync(TenantId, CashierId, request);
        await svc.CreateSaleAsync(TenantId, CashierId, request);

        Assert.Equal(2, pos.Transactions.Count);
    }

    [Fact]
    public async Task CreateSale_stamps_register_and_rejects_shift_of_another_register()
    {
        var registerId = Guid.NewGuid();
        var (pos, stock, catalog, shift, _) = SaleSeeder.Seed("REG-1", registerId: registerId);
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var request = new CreateSaleRequest(shift.Id, [new SaleItemRequest("REG-1", 1)], "Cash", 10);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId, request, registerId: registerId);
        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(registerId, pos.Transactions.Single().CashRegisterId);

        var (rejected, err2, status) = await svc.CreateSaleAsync(
            TenantId, CashierId, request, registerId: Guid.NewGuid());
        Assert.Null(rejected);
        Assert.Equal(409, status);
        Assert.NotNull(err2);
    }

    [Fact]
    public async Task OpenShift_is_scoped_per_register()
    {
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos);
        var regA = Guid.NewGuid();
        var regB = Guid.NewGuid();

        var (a, errA, _) = await svc.OpenShiftAsync(TenantId, CashierId, new OpenShiftRequest(StoreId), registerId: regA);
        var (b, errB, _) = await svc.OpenShiftAsync(TenantId, CashierId, new OpenShiftRequest(StoreId), registerId: regB);
        var (dup, _, dupStatus) = await svc.OpenShiftAsync(TenantId, CashierId, new OpenShiftRequest(StoreId), registerId: regA);

        Assert.Null(errA);
        Assert.Null(errB);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Null(dup);
        Assert.Equal(409, dupStatus);
        Assert.Equal(regA, pos.Shifts[0].RegisterId);
        Assert.Equal(2, pos.Shifts.Count);
    }

    [Fact]
    public async Task BatchSales_reports_partial_failure_and_duplicates_per_item()
    {
        var (pos, stock, catalog, shift, batch) = SaleSeeder.Seed("BATCH-1");
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var okId = Guid.NewGuid();
        var badId = Guid.NewGuid();
        var batchRequest = new BatchSalesRequest(
        [
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("BATCH-1", 1)], "Cash", 10, ClientTransactionId: okId),
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("NO-SUCH-BARCODE", 1)], "Cash", 10, ClientTransactionId: badId),
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("BATCH-1", 1)], "Cash", 10, ClientTransactionId: okId), // replay
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("BATCH-1", 1)], "Cash", 10), // no client id
        ]);

        var (result, error, _) = await svc.CreateSalesBatchAsync(TenantId, CashierId, batchRequest);

        Assert.Null(error);
        var r = result!.Results;
        Assert.Equal(4, r.Count);
        Assert.Equal("created", r[0].Status);
        Assert.Equal("failed", r[1].Status);
        Assert.Equal(400, r[1].ErrorStatusCode);
        Assert.Equal("duplicate", r[2].Status);
        Assert.Equal(r[0].Sale!.TransactionId, r[2].Sale!.TransactionId);
        Assert.Equal("failed", r[3].Status);
        Assert.Equal(400, r[3].ErrorStatusCode);

        Assert.Single(pos.Transactions);   // only the first item persisted a sale
        Assert.Equal(9m, batch.Quantity);  // sold once
        Assert.True(pos.DiscardCount >= 1); // the failed item's pending changes were dropped
    }

    [Fact]
    public async Task BatchSales_rejects_empty_and_oversized_batches()
    {
        var svc = BuildService();

        var (_, emptyErr, emptyStatus) = await svc.CreateSalesBatchAsync(TenantId, CashierId, new BatchSalesRequest([]));
        Assert.Equal(400, emptyStatus);
        Assert.NotNull(emptyErr);

        var tooMany = Enumerable.Range(0, PosService.MaxBatchSales + 1)
            .Select(_ => new CreateSaleRequest(Guid.NewGuid(), [new SaleItemRequest("X", 1)], "Cash", 1, ClientTransactionId: Guid.NewGuid()))
            .ToList();
        var (_, bigErr, bigStatus) = await svc.CreateSalesBatchAsync(TenantId, CashierId, new BatchSalesRequest(tooMany));
        Assert.Equal(400, bigStatus);
        Assert.NotNull(bigErr);
    }
    // ── Idempotency hardening: a reused key must not "succeed" with a different sale ─────

    [Fact]
    public async Task Replay_with_different_items_is_409_not_the_stored_sale()
    {
        var (pos, stock, catalog, shift, _) = SaleSeeder.Seed("KEY-1");
        var other = new Item { Id = Guid.NewGuid(), TenantId = TenantId, Barcodes = ["KEY-2"], Name = "Other", PriceRetail = 5m };
        catalog.Products.Add(other);
        stock.Batches.Add(new ProductStock
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProductId = other.Id, StoreId = StoreId,
            Quantity = 10, QuantityInitial = 10,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Status = "safe",
        });
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var key = Guid.NewGuid();

        var (first, e1, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("KEY-1", 1)], "Cash", 10, ClientTransactionId: key));
        Assert.Null(e1);

        var differentItem = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("KEY-2", 1)], "Cash", 10, ClientTransactionId: key));
        var differentQty = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("KEY-1", 3)], "Cash", 30, ClientTransactionId: key));
        var differentPayment = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("KEY-1", 1)], "Card", 10, ClientTransactionId: key));

        foreach (var r in new[] { differentItem, differentQty, differentPayment })
        {
            Assert.Null(r.Sale);
            Assert.Equal(409, r.StatusCode);
        }

        Assert.Single(pos.Transactions);
        Assert.Equal(first!.TransactionId, pos.Transactions[0].Id);
    }

    [Fact]
    public async Task Replay_from_another_register_is_409_and_batch_reports_it_as_failed()
    {
        var registerA = Guid.NewGuid();
        var (pos, stock, catalog, shift, _) = SaleSeeder.Seed("KEY-REG", registerId: registerA);
        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);
        var key = Guid.NewGuid();
        var request = new CreateSaleRequest(shift.Id, [new SaleItemRequest("KEY-REG", 1)], "Cash", 10, ClientTransactionId: key);

        var (sale, err, _) = await svc.CreateSaleAsync(TenantId, CashierId, request, registerId: registerA);
        Assert.Null(err);

        var sameRegister = await svc.CreateSaleAsync(TenantId, CashierId, request, registerId: registerA);
        Assert.Equal(sale!.TransactionId, sameRegister.Sale!.TransactionId);

        var otherRegister = await svc.CreateSaleAsync(TenantId, CashierId, request, registerId: Guid.NewGuid());
        Assert.Equal(409, otherRegister.StatusCode);

        var (batch, _, _) = await svc.CreateSalesBatchAsync(
            TenantId, CashierId, new BatchSalesRequest([request]), registerId: Guid.NewGuid());
        var item = Assert.Single(batch!.Results);
        Assert.Equal("failed", item.Status);
        Assert.Equal(409, item.ErrorStatusCode);
        Assert.Single(pos.Transactions);
    }
    // ── Offline sync attribution (TASK-730) ────────────────────────────────

    private sealed class Attribution
    {
        public Guid RegisterId { get; } = Guid.NewGuid();
        public List<RegisterSession> Sessions { get; } = [];
        public Dictionary<Guid, User> Users { get; } = [];
        public PosService Svc { get; }
        public FakePosRepoHandle Pos { get; }
        public PosShift Shift { get; }
        public ProductStock Batch { get; }
        public readonly Guid TokenCashier = Guid.NewGuid();

        public Attribution(string barcode)
        {
            var (pos, stock, catalog, shift, batch) = SaleSeeder.Seed(barcode, stockQty: 50m, registerId: RegisterId);
            Shift = shift;
            Batch = batch;
            Pos = new FakePosRepoHandle(pos);

            var registers = Substitute.For<ICashRegisterRepository>();
            registers.GetByIdAsync(RegisterId, Arg.Any<CancellationToken>())
                .Returns(new CashRegister { Id = RegisterId, TenantId = TenantId, StoreId = StoreId, Number = 1, Name = "r" });
            registers.HadSessionAtAsync(default, default, default, default, default).ReturnsForAnyArgs(ci =>
            {
                var (reg, cashier, at, tol) = (ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<DateTime>(2), ci.ArgAt<TimeSpan>(3));
                return Sessions.Any(x => x.RegisterId == reg && x.CashierId == cashier && x.StartedAt <= at
                                         && (x.EndedAt == null || x.EndedAt >= at - tol));
            });

            var users = Substitute.For<IUserRepository>();
            users.GetByIdAsync(default, default).ReturnsForAnyArgs(ci => Users.GetValueOrDefault(ci.ArgAt<Guid>(0)));

            Svc = BuildService(pos: pos, stock: stock, catalog: catalog, users: users, registers: registers);
        }

        public User AddUser(string role = AppRoles.Cashier, Guid? storeId = null, bool active = true)
        {
            var u = User.Create(TenantId, $"{Guid.NewGuid():N}@x.test", "Colleague", "pw", role, storeId ?? StoreId);
            if (!active) u.Deactivate();
            Users[u.Id] = u;
            return u;
        }

        public void AddSession(Guid cashier, DateTime start, DateTime? end = null) =>
            Sessions.Add(new RegisterSession { TenantId = TenantId, RegisterId = RegisterId, CashierId = cashier, StartedAt = start, EndedAt = end });

        public CreateSaleRequest Sale(string barcode, Guid? cashier = null, DateTime? at = null, Guid? key = null) =>
            new(Shift.Id, [new SaleItemRequest(barcode, 1)], "Cash", 10,
                ClientTransactionId: key ?? Guid.NewGuid(), CashierId: cashier, OccurredAt: at);

        public async Task<BatchSaleItemResultDto> Flush(CreateSaleRequest sale)
        {
            var (result, error, _) = await Svc.CreateSalesBatchAsync(
                TenantId, TokenCashier, new BatchSalesRequest([sale]), registerId: RegisterId);
            Assert.Null(error);
            return Assert.Single(result!.Results);
        }
    }

    // The repo is a file-local fake; expose just what the attribution tests read.
    private sealed class FakePosRepoHandle(object inner)
    {
        public IReadOnlyList<PosTransaction> Transactions => AttributionAccess.Transactions(inner);
        public IReadOnlyList<StockEvent> Events => AttributionAccess.Events(inner);
    }

    [Fact]
    public async Task Batch_sale_of_cashier_A_flushed_by_B_is_recorded_under_A()
    {
        var t = new Attribution("ATTR-1");
        var a = t.AddUser();
        var now = DateTime.UtcNow;
        t.AddSession(a.Id, now.AddHours(-3), now.AddHours(-1));          // A worked, then B signed in
        t.AddSession(t.TokenCashier, now.AddHours(-1));

        var item = await t.Flush(t.Sale("ATTR-1", cashier: a.Id, at: now.AddHours(-2)));

        Assert.Equal("created", item.Status);
        var tx = Assert.Single(t.Pos.Transactions);
        Assert.Equal(a.Id, tx.CashierId);
        Assert.All(t.Pos.Events, e => Assert.Equal(a.Id, e.PerformedBy));
    }

    [Fact]
    public async Task Batch_sale_without_or_with_own_CashierId_is_recorded_under_the_token_cashier()
    {
        var t = new Attribution("ATTR-2");

        await t.Flush(t.Sale("ATTR-2"));
        await t.Flush(t.Sale("ATTR-2", cashier: t.TokenCashier));

        Assert.All(t.Pos.Transactions, tx => Assert.Equal(t.TokenCashier, tx.CashierId));
        Assert.Equal(2, t.Pos.Transactions.Count);
    }

    [Fact]
    public async Task Batch_sale_for_a_cashier_with_no_session_on_this_register_fails()
    {
        var t = new Attribution("ATTR-3");
        var stranger = t.AddUser();

        var item = await t.Flush(t.Sale("ATTR-3", cashier: stranger.Id, at: DateTime.UtcNow.AddMinutes(-10)));

        Assert.Equal("failed", item.Status);
        Assert.Equal(403, item.ErrorStatusCode);
        Assert.Empty(t.Pos.Transactions);
    }

    [Fact]
    public async Task Batch_attribution_respects_the_session_window_with_5_minute_end_tolerance()
    {
        var t = new Attribution("ATTR-4");
        var a = t.AddUser();
        var now = DateTime.UtcNow;
        t.AddSession(a.Id, now.AddHours(-3), now.AddHours(-2));

        var before = await t.Flush(t.Sale("ATTR-4", a.Id, now.AddHours(-4)));                         // before session start
        var withinTolerance = await t.Flush(t.Sale("ATTR-4", a.Id, now.AddHours(-2).AddMinutes(3)));  // 3 min after end
        var afterTolerance = await t.Flush(t.Sale("ATTR-4", a.Id, now.AddHours(-2).AddMinutes(10)));  // 10 min after end

        Assert.Equal("failed", before.Status);
        Assert.Equal("created", withinTolerance.Status);
        Assert.Equal("failed", afterTolerance.Status);
    }

    [Fact]
    public async Task Batch_attribution_without_OccurredAt_uses_now_and_needs_an_open_or_just_ended_session()
    {
        var t = new Attribution("ATTR-5");
        var open = t.AddUser();
        var longGone = t.AddUser();
        t.AddSession(open.Id, DateTime.UtcNow.AddHours(-1));
        t.AddSession(longGone.Id, DateTime.UtcNow.AddHours(-5), DateTime.UtcNow.AddHours(-4));

        Assert.Equal("created", (await t.Flush(t.Sale("ATTR-5", open.Id))).Status);
        Assert.Equal("failed", (await t.Flush(t.Sale("ATTR-5", longGone.Id))).Status);
    }

    [Fact]
    public async Task Batch_attribution_rejects_other_store_inactive_and_non_pos_users()
    {
        var t = new Attribution("ATTR-6");
        var now = DateTime.UtcNow;
        var otherStore = t.AddUser(storeId: Guid.NewGuid());
        var inactive = t.AddUser(active: false);
        var merch = t.AddUser(role: AppRoles.Merchandiser);
        foreach (var u in new[] { otherStore, inactive, merch })
            t.AddSession(u.Id, now.AddHours(-2)); // even with a session row, the user checks fail

        foreach (var u in new[] { otherStore.Id, inactive.Id, merch.Id, Guid.NewGuid() /* unknown */ })
        {
            var item = await t.Flush(t.Sale("ATTR-6", u, now.AddMinutes(-30)));
            Assert.Equal("failed", item.Status);
            Assert.Equal(403, item.ErrorStatusCode);
        }

        Assert.Empty(t.Pos.Transactions);
    }

    [Fact]
    public async Task Batch_attribution_rejects_future_and_too_old_OccurredAt()
    {
        var t = new Attribution("ATTR-7");
        var a = t.AddUser();
        var now = DateTime.UtcNow;
        t.AddSession(a.Id, now.AddDays(-30));

        var future = await t.Flush(t.Sale("ATTR-7", a.Id, now.AddMinutes(10)));
        var skewOk = await t.Flush(t.Sale("ATTR-7", a.Id, now.AddMinutes(3)));
        var tooOld = await t.Flush(t.Sale("ATTR-7", a.Id, now.AddDays(-15)));
        var oldButOk = await t.Flush(t.Sale("ATTR-7", a.Id, now.AddDays(-13)));

        Assert.Equal("failed", future.Status);
        Assert.Equal("created", skewOk.Status);
        Assert.Equal("failed", tooOld.Status);
        Assert.Equal("created", oldButOk.Status);
    }

    [Fact]
    public async Task Batch_attribution_needs_a_register_token()
    {
        var t = new Attribution("ATTR-8");
        var a = t.AddUser();
        t.AddSession(a.Id, DateTime.UtcNow.AddHours(-1));

        var (result, _, _) = await t.Svc.CreateSalesBatchAsync(
            TenantId, t.TokenCashier, new BatchSalesRequest([t.Sale("ATTR-8", a.Id)]), registerId: null);

        Assert.Equal("failed", result!.Results.Single().Status);
        Assert.Empty(t.Pos.Transactions);
    }

    [Fact]
    public async Task Single_sale_with_a_foreign_CashierId_is_400_and_own_id_is_fine_and_OccurredAt_is_ignored()
    {
        var t = new Attribution("ATTR-9");
        var someoneElse = t.AddUser();

        var foreign = await t.Svc.CreateSaleAsync(TenantId, t.TokenCashier,
            t.Sale("ATTR-9", cashier: someoneElse.Id), registerId: t.RegisterId);
        Assert.Equal(400, foreign.StatusCode);
        Assert.Empty(t.Pos.Transactions);

        var own = await t.Svc.CreateSaleAsync(TenantId, t.TokenCashier,
            t.Sale("ATTR-9", cashier: t.TokenCashier, at: DateTime.UtcNow.AddDays(-400)), registerId: t.RegisterId);
        Assert.Null(own.Error); // absurd OccurredAt is simply ignored here
        Assert.Equal(t.TokenCashier, t.Pos.Transactions.Single().CashierId);
    }

    [Fact]
    public async Task Replay_keeps_its_original_cashier_and_a_different_CashierId_is_a_mismatch()
    {
        var t = new Attribution("ATTR-10");
        var a = t.AddUser();
        var c = t.AddUser();
        var now = DateTime.UtcNow;
        t.AddSession(a.Id, now.AddHours(-3), now.AddHours(-1));
        t.AddSession(c.Id, now.AddHours(-3), now.AddHours(-1));
        var key = Guid.NewGuid();

        var first = await t.Flush(t.Sale("ATTR-10", a.Id, now.AddHours(-2), key));
        var sameReplay = await t.Flush(t.Sale("ATTR-10", a.Id, now.AddHours(-2), key));
        var otherCashierReplay = await t.Flush(t.Sale("ATTR-10", c.Id, now.AddHours(-2), key));

        Assert.Equal("created", first.Status);
        Assert.Equal("duplicate", sameReplay.Status);
        Assert.Equal(first.Sale!.TransactionId, sameReplay.Sale!.TransactionId);
        Assert.Equal("failed", otherCashierReplay.Status);
        Assert.Equal(409, otherCashierReplay.ErrorStatusCode);
        Assert.Single(t.Pos.Transactions);
        Assert.Equal(a.Id, t.Pos.Transactions[0].CashierId);
    }
}

// ── Promotion rules (promotion-rules.md, Phase 1) ──────────────────────────

file sealed class FakePromotionRepo : IPromotionRuleRepository
{
    public List<PublishedPromotionRuleRow> Published { get; } = [];
    public List<PromotionRuleApplication> Applications { get; } = [];

    public Task<IReadOnlyList<PublishedPromotionRuleRow>> GetPublishedAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PublishedPromotionRuleRow>>(Published);

    public Task AddApplicationsAsync(IEnumerable<PromotionRuleApplication> applications, CancellationToken ct = default)
    {
        Applications.AddRange(applications);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PromotionRuleApplication>> GetApplicationsForTransactionAsync(Guid transactionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PromotionRuleApplication>>(Applications.Where(a => a.TransactionId == transactionId).ToList());

    public Task<PromotionRule?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<PromotionRule?>(null);
    public Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsAsync(Guid ruleId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PromotionRuleVersion>>([]);
    public Task AddRuleAsync(PromotionRule rule, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddVersionAsync(PromotionRuleVersion version, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public PublishedPromotionRuleRow Add(string name, string definitionJson)
    {
        var row = new PublishedPromotionRuleRow(Guid.NewGuid(), name, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), definitionJson);
        Published.Add(row);
        return row;
    }
}

file sealed class PromoCtx
{
    public FakePosRepo Pos { get; } = new();
    public FakeStockRepo Stock { get; } = new();
    public FakeCatalogRepo Catalog { get; } = new();
    public FakeLoyaltyRepo Loyalty { get; } = new();
    public FakePromotionRepo Promotions { get; } = new();
    public PosShift Shift { get; }

    public PromoCtx()
    {
        Shift = new PosShift { TenantId = PosServiceTestsIds.TenantId, StoreId = PosServiceTestsIds.StoreId };
        Pos.Shifts.Add(Shift);
    }

    public Item AddProduct(string barcode, decimal price)
    {
        var p = new Item { Id = Guid.NewGuid(), TenantId = PosServiceTestsIds.TenantId, Barcodes = [barcode], Name = "P-" + barcode, PriceRetail = price };
        Catalog.Products.Add(p);
        return p;
    }

    public ProductStock AddBatch(Guid productId, decimal qty, int days)
    {
        var b = new ProductStock
        {
            Id = Guid.NewGuid(), TenantId = PosServiceTestsIds.TenantId, ProductId = productId, StoreId = PosServiceTestsIds.StoreId,
            Quantity = qty, QuantityInitial = qty,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days)), Status = "safe",
        };
        Stock.Batches.Add(b);
        return b;
    }

    public PosService Service(bool withPromotions = true) => new(
        Pos, Stock, Catalog, new FakeDiscountRepo(), new FakeFiscalFactory(new NoopFiscalService()),
        Loyalty, new FakeCustomerRepo(), NullLogger<PosService>.Instance,
        promotions: withPromotions ? Promotions : null);

    public static (LoyaltyMembership Membership, PromoCtx T, Item Product) WithTier(decimal tierPercent)
    {
        var t = new PromoCtx();
        var product = t.AddProduct("TIERPROMO", 100m);
        t.AddBatch(product.Id, 10m, days: 10);
        var tier = new LoyaltyTierDefinition
        {
            TenantId = PosServiceTestsIds.TenantId, Name = "Gold", SortOrder = 1, AccrualMultiplier = 0m, DiscountPercent = tierPercent,
        };
        var membership = new LoyaltyMembership
        {
            TenantId = PosServiceTestsIds.TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active,
            CurrentTierId = tier.Id, CurrentTier = tier,
        };
        t.Loyalty.Memberships.Add(membership);
        t.Loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = PosServiceTestsIds.TenantId, IsEnabled = true, AccrualRatePercent = 0m });
        return (membership, t, product);
    }

    public PosTransaction SingleTx => Pos.Transactions.Single();
    public IReadOnlyList<StockEvent> Events => Pos.Events;
}

public sealed class PosServicePromotionTests
{
    private static readonly Guid TenantId = PosServiceTestsIds.TenantId;
    private static readonly Guid StoreId = PosServiceTestsIds.StoreId;
    private static readonly Guid CashierId = Guid.NewGuid();

    private static string TwoPlusOne(Guid itemId) =>
        $$$"""{"type":"buy_n_get_m","targets":{"itemIds":["{{{itemId}}}"]},"params":{"buyQty":2,"getQty":1,"getPrice":0.01}}""";

    private static string PercentOff(Guid itemId, decimal percent) =>
        $$$"""{"type":"percent_off","targets":{"itemIds":["{{{itemId}}}"]},"params":{"percent":{{{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}""";

    [Fact]
    public async Task Two_plus_one_splits_the_line_into_sub_lines_keeps_FEFO_logs_and_replays_idempotently()
    {
        var t = new PromoCtx();
        var product = t.AddProduct("PROMO-21", 30m);
        var early = t.AddBatch(product.Id, 2m, days: 5);
        var late = t.AddBatch(product.Id, 10m, days: 30);
        var rule = t.Promotions.Add("2+1", TwoPlusOne(product.Id));
        var svc = t.Service();
        var request = new CreateSaleRequest(t.Shift.Id, [new SaleItemRequest("PROMO-21", 3)], "Cash", 100m,
            ClientTransactionId: Guid.NewGuid());

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);

        Assert.Null(error);
        Assert.Equal(60.01m, sale!.Subtotal);
        Assert.Equal(39.99m, sale.Change);
        var tx = t.SingleTx;
        Assert.Equal(60.01m, tx.TotalAmount);
        Assert.Equal(tx.TotalAmount, tx.Items.Sum(i => Math.Round(i.PriceFinal * i.Quantity, 2)));
        Assert.Equal(2, tx.Items.Count);

        var regular = tx.Items.ElementAt(0);
        Assert.Equal((2m, 30m, 0m, 30m), (regular.Quantity, regular.PriceRetail, regular.DiscountAmount, regular.PriceFinal));
        Assert.Null(regular.PromotionRuleId);
        Assert.Equal(early.Id, regular.ProductStockId);

        var promo = tx.Items.ElementAt(1);
        Assert.Equal((1m, 30m, 29.99m, 0.01m), (promo.Quantity, promo.PriceRetail, promo.DiscountAmount, promo.PriceFinal));
        Assert.Equal(rule.RuleId, promo.PromotionRuleId);
        Assert.Equal("2+1", promo.PromotionLabel);
        Assert.Equal(late.Id, promo.ProductStockId); // 3rd unit came from the later batch

        // FEFO for the total quantity: 2 from the early batch, 1 from the later one.
        Assert.Equal(0m, early.Quantity);
        Assert.Equal(9m, late.Quantity);
        Assert.Equal([-2m, -1m], t.Events.Select(e => e.QuantityDelta).ToArray());

        Assert.Equal(new[] { "2+1" }, sale.Items.Where(i => i.PromotionLabel is not null).Select(i => i.PromotionLabel!).ToArray());

        var app = Assert.Single(t.Promotions.Applications);
        Assert.Equal((rule.RuleId, rule.VersionId, tx.Id, StoreId), (app.RuleId, app.VersionId, app.TransactionId, app.StoreId));
        Assert.Equal(1m, app.Units);
        Assert.Equal(29.99m, app.DiscountAmount);

        // Idempotent replay: the same request (1 line) matches the stored 2 sub-lines.
        var (again, againError, _) = await svc.CreateSaleAsync(TenantId, CashierId, request);
        Assert.Null(againError);
        Assert.Equal(sale.TransactionId, again!.TransactionId);
        Assert.Equal(60.01m, again.Subtotal);
        Assert.Single(t.Pos.Transactions);
        Assert.Single(t.Promotions.Applications);
        Assert.Equal(9m, late.Quantity);

        var batch = await svc.CreateSalesBatchAsync(TenantId, CashierId, new BatchSalesRequest([request]));
        Assert.Equal("duplicate", Assert.Single(batch.Result!.Results).Status);

        // A replay with another quantity is still a conflict.
        var (_, conflictError, conflictStatus) = await svc.CreateSaleAsync(TenantId, CashierId,
            request with { Items = [new SaleItemRequest("PROMO-21", 4)] });
        Assert.NotNull(conflictError);
        Assert.Equal(409, conflictStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sale_without_applicable_rules_is_identical_to_the_legacy_path(bool withRules)
    {
        var t = new PromoCtx();
        var product = t.AddProduct("PLAIN", 12.35m);
        t.AddBatch(product.Id, 10m, days: 10);
        if (withRules)
        {
            t.Promotions.Add("other item", PercentOff(Guid.NewGuid(), 50m));
            t.Promotions.Add("other store", $$$"""{"type":"percent_off","targets":{"itemIds":["{{{product.Id}}}"]},"params":{"percent":50},"scope":{"storeIds":["{{{Guid.NewGuid()}}}"]}}""");
            t.Promotions.Add("expired", $$$"""{"type":"percent_off","targets":{"itemIds":["{{{product.Id}}}"]},"params":{"percent":50},"scope":{"validTo":"2020-01-01T00:00:00Z"}}""");
            t.Promotions.Add("2+1 needs 3", TwoPlusOne(product.Id));
            t.Promotions.Add("broken json", "{oops");
        }

        var (sale, error, _) = await t.Service(withPromotions: withRules).CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(t.Shift.Id, [new SaleItemRequest("PLAIN", 2)], "Card", 24.70m));

        Assert.Null(error);
        var item = Assert.Single(t.SingleTx.Items);
        Assert.Equal((2m, 12.35m, 0m, 12.35m), (item.Quantity, item.PriceRetail, item.DiscountAmount, item.PriceFinal));
        Assert.Null(item.PromotionRuleId);
        Assert.Null(item.PromotionLabel);
        Assert.Equal(24.70m, t.SingleTx.TotalAmount);
        Assert.Equal(4.12m, t.SingleTx.TaxAmount);
        Assert.Equal(24.70m, sale!.Subtotal);
        Assert.Empty(t.Promotions.Applications);
    }

    [Fact]
    public async Task Loyalty_tier_competes_with_a_promo_and_the_cheaper_one_wins_without_stacking()
    {
        // Tier 25% vs promo 10%: tier wins, line priced exactly like the legacy tier path.
        var (m1, t1, p1) = PromoCtx.WithTier(25m);
        t1.Promotions.Add("-10%", PercentOff(p1.Id, 10m));
        var (s1, e1, _) = await t1.Service().CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(t1.Shift.Id, [new SaleItemRequest("TIERPROMO", 1)], "Cash", 100m, LoyaltyMembershipId: m1.Id));
        Assert.Null(e1);
        var i1 = Assert.Single(t1.SingleTx.Items);
        Assert.Equal((25m, 75m), (i1.DiscountAmount, i1.PriceFinal));
        Assert.Null(i1.PromotionRuleId);
        Assert.Equal(75m, s1!.Subtotal);
        Assert.Empty(t1.Promotions.Applications);

        // Tier 25% vs promo 30%: promo wins, the tier is NOT added on top (70, not 45).
        var (m2, t2, p2) = PromoCtx.WithTier(25m);
        var rule = t2.Promotions.Add("-30%", PercentOff(p2.Id, 30m));
        var (s2, e2, _) = await t2.Service().CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(t2.Shift.Id, [new SaleItemRequest("TIERPROMO", 1)], "Cash", 100m, LoyaltyMembershipId: m2.Id));
        Assert.Null(e2);
        var i2 = Assert.Single(t2.SingleTx.Items);
        Assert.Equal((30m, 70m), (i2.DiscountAmount, i2.PriceFinal));
        Assert.Equal(rule.RuleId, i2.PromotionRuleId);
        Assert.Equal(70m, s2!.Subtotal);
        Assert.Equal(30m, Assert.Single(t2.Promotions.Applications).DiscountAmount);
    }

    [Fact]
    public async Task Loyalty_only_rule_needs_a_membership()
    {
        var t = new PromoCtx();
        var product = t.AddProduct("MEMBERS", 50m);
        t.AddBatch(product.Id, 10m, days: 10);
        t.Promotions.Add("members", $$$"""{"type":"percent_off","targets":{"itemIds":["{{{product.Id}}}"]},"params":{"percent":20},"audience":{"kind":"loyalty_member"}}""");

        var (sale, error, _) = await t.Service().CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(t.Shift.Id, [new SaleItemRequest("MEMBERS", 1)], "Cash", 50m));

        Assert.Null(error);
        Assert.Equal(50m, sale!.Subtotal);
        Assert.Null(Assert.Single(t.SingleTx.Items).PromotionRuleId);
    }

    [Fact]
    public async Task Batch_sale_is_priced_at_its_OccurredAt()
    {
        var t = new PromoCtx();
        var product = t.AddProduct("WINDOW", 40m);
        t.AddBatch(product.Id, 10m, days: 10);
        var end = DateTime.UtcNow.AddHours(-1);
        t.Promotions.Add("ended an hour ago", $$$"""{"type":"percent_off","targets":{"itemIds":["{{{product.Id}}}"]},"params":{"percent":50},"scope":{"validTo":"{{{end:O}}}"}}""");

        var during = new CreateSaleRequest(t.Shift.Id, [new SaleItemRequest("WINDOW", 1)], "Cash", 40m,
            ClientTransactionId: Guid.NewGuid(), OccurredAt: end.AddMinutes(-30));
        var after = during with { ClientTransactionId = Guid.NewGuid(), OccurredAt = DateTime.UtcNow.AddMinutes(-1) };

        var batch = await t.Service().CreateSalesBatchAsync(TenantId, CashierId, new BatchSalesRequest([during, after]));

        Assert.Equal(["created", "created"], batch.Result!.Results.Select(r => r.Status).ToArray());
        Assert.Equal(20m, batch.Result.Results[0].Sale!.Subtotal);
        Assert.Equal(40m, batch.Result.Results[1].Sale!.Subtotal);
    }
}
