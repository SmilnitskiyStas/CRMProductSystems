using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-644 / KI-036 — headline regression for the cross-tenant marketplace RLS leak
/// (<c>MarketplaceRepository.SetProviderRoleAsync</c> issued a SESSION-level
/// <c>SET app.role = 'provider'</c> on a manually opened <see cref="System.Data.Common.DbConnection"/>
/// and never reset it, so every later statement of the same HTTP request — including the
/// <c>items</c> lookups in <see cref="MarketplaceOrderService.CheckCatalogConflictsAsync"/> /
/// <see cref="MarketplaceOrderService.CreateOrderAsync"/> — ran with a full cross-tenant read+write
/// bypass). Reported symptom: a client tenant with an <b>empty</b> item catalog was shown a
/// «Знайдено збіги штрихкодів» dialog listing a <b>foreign tenant's</b> <c>Item</c> (id, name,
/// image, barcodes); the same primary key then armed a cross-tenant WRITE vector via
/// <c>catalogAction:"link"</c>.
///
/// This suite drives the <b>real</b> <see cref="MarketplaceOrderService"/> composed from the real
/// <see cref="MarketplaceRepository"/> + <see cref="ItemRepository"/> + <see cref="ProviderRlsOverride"/>
/// (NSubstitute only for the non-RLS collaborators) against live dev Postgres on port 5435, under a
/// genuine <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS) session — so the fix's
/// <c>SET LOCAL app.role = 'provider'</c>-inside-one-transaction behaviour is exercised under real
/// RLS enforcement, not a pass-through double. Same harness as
/// <see cref="SupplierAgreementMarkSignedRlsIntegrationTests"/> (TASK-582/643): part of the
/// <c>TENANT_ISOLATION_TESTS</c> collection, <c>rls_audit_test_role</c> owned by
/// <see cref="RlsAuditRoleFixture"/>, soft-skips when Postgres is unreachable,
/// <c>NpgsqlDataSourceBuilder(...).EnableDynamicJson()</c> for the <c>Item.Barcodes</c> jsonb mapping.
///
/// The headline test (<see cref="CheckCatalogConflicts_under_client_session_ignores_foreign_tenant_barcode_and_does_not_leak_provider_role"/>)
/// FAILS on pre-fix sources — see the TASK-644 task log for the recorded pre-fix failure output.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MarketplaceOrderCatalogConflictsRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");
    private string Barcode => $"BC-644-{_run}";

    public MarketplaceOrderCatalogConflictsRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _connectionString = _fixture.ConnectionString;

        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping marketplace catalog-conflicts RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
            return Task.CompletedTask;
        }

        try
        {
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_dataSource)
                .IgnoreManyServiceProvidersWarning()
                .Options;
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping marketplace catalog-conflicts RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── Headline regression (fails on pre-fix code) ───────────────────────────

    [Fact]
    public async Task CheckCatalogConflicts_under_client_session_ignores_foreign_tenant_barcode_and_does_not_leak_provider_role()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedFixtureAsync();
        try
        {
            await using var session = await OpenSessionAsync(f.ClientTenantId);
            var service = BuildOrderService(session.Db);

            var (conflicts, error, isGateViolation) = await service.CheckCatalogConflictsAsync(
                f.ClientTenantId, f.SupplierId,
                [new CreateMarketplaceOrderItemDto(f.SupplierItemId, 1)]);

            // Pre-fix: this returned ONE conflict pointing at the third tenant's Item
            // (id/name/imageUrl/barcodes) even though the client's own catalog is empty.
            Assert.Null(error);
            Assert.False(isGateViolation);
            Assert.NotNull(conflicts);
            Assert.Empty(conflicts!);

            // Highest-value assertion: the provider bypass must not have survived past the
            // repository call. Checked on the SAME still-open connection the service just used.
            Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));

            // A direct barcode lookup on that same session sees zero rows — RLS is intact.
            var direct = await new ItemRepository(session.Db).GetByAnyBarcodeAsync([Barcode]);
            Assert.Empty(direct);
        }
        finally
        {
            await CleanupAsync(f.AllTenantIds);
        }
    }

    [Fact]
    public async Task CreateOrder_under_client_session_provisions_exactly_one_own_tenant_item()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedFixtureAsync();
        try
        {
            MarketplaceOrderDto? order;
            await using (var session = await OpenSessionAsync(f.ClientTenantId))
            {
                var service = BuildOrderService(session.Db);

                var (dto, error, _) = await service.CreateOrderAsync(
                    f.ClientTenantId, f.SupplierId,
                    new CreateMarketplaceOrderDto(
                        [new CreateMarketplaceOrderItemDto(f.SupplierItemId, 2)],
                        Comment: null,
                        DestinationStoreId: f.ClientLocationId),
                    f.ClientUserId);

                // No bogus BarcodeCollisionError from the third tenant's row.
                Assert.Null(error);
                Assert.NotNull(dto);
                order = dto;

                Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
            }

            await using var verify = NewContext();
            var provisioned = await verify.Items.AsNoTracking()
                .Where(i => i.TenantId == f.ClientTenantId && i.SourceSupplierItemId == f.SupplierItemId)
                .ToListAsync();
            var item = Assert.Single(provisioned);
            Assert.Contains(Barcode, item.Barcodes);
            Assert.Equal("Товар постачальника", item.Name);

            // The third tenant's row is untouched.
            var third = await verify.Items.AsNoTracking().SingleAsync(i => i.Id == f.ThirdItemId);
            Assert.Null(third.SourceSupplierItemId);

            // Exactly one marketplace order for this client.
            var orders = await verify.MarketplaceOrders.AsNoTracking()
                .CountAsync(o => o.ClientTenantId == f.ClientTenantId);
            Assert.Equal(1, orders);
            Assert.NotNull(order);
        }
        finally
        {
            await CleanupAsync(f.AllTenantIds);
        }
    }

    // ── Negative control: a genuine own-tenant collision is still reported ─────

    [Fact]
    public async Task CheckCatalogConflicts_reports_the_conflict_when_the_client_tenant_itself_owns_the_barcode()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedFixtureAsync(clientOwnsBarcode: true);
        try
        {
            await using var session = await OpenSessionAsync(f.ClientTenantId);
            var service = BuildOrderService(session.Db);

            var (conflicts, error, _) = await service.CheckCatalogConflictsAsync(
                f.ClientTenantId, f.SupplierId,
                [new CreateMarketplaceOrderItemDto(f.SupplierItemId, 1)]);

            Assert.Null(error);
            Assert.NotNull(conflicts);
            var conflict = Assert.Single(conflicts!);
            Assert.Equal(f.SupplierItemId, conflict.SupplierItemId);
            Assert.Equal(f.ClientOwnItemId, conflict.ExistingItem.Id);
            Assert.Equal("Мій власний товар", conflict.ExistingItem.Name);
            Assert.Contains(Barcode, conflict.ExistingItem.Barcodes);
        }
        finally
        {
            await CleanupAsync(f.AllTenantIds);
        }
    }

    // ── F2 write-vector negative control (TASK-641 R6) ────────────────────────

    [Fact]
    public async Task CreateOrder_link_to_a_foreign_tenant_item_is_rejected_and_never_writes_to_that_tenant()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedFixtureAsync(thirdItemHasGraph: true);
        try
        {
            await using (var session = await OpenSessionAsync(f.ClientTenantId))
            {
                var service = BuildOrderService(session.Db);

                var (dto, error, _) = await service.CreateOrderAsync(
                    f.ClientTenantId, f.SupplierId,
                    new CreateMarketplaceOrderDto(
                        [new CreateMarketplaceOrderItemDto(f.SupplierItemId, 1, "link", f.ThirdItemId)],
                        Comment: null,
                        DestinationStoreId: f.ClientLocationId),
                    f.ClientUserId);

                Assert.Null(dto);
                // Reported exactly like "not found" — never confirms another tenant owns that id.
                Assert.Equal(MarketplaceOrderService.LinkedItemNotFoundError, error);
                Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
            }

            // The third tenant's Item and its loaded graph (categories / suppliers) are byte-for-byte
            // unchanged — pre-fix, _items.Update marked the whole .Include'd graph Modified, so this
            // was a 4-table cross-tenant full-row rewrite, not one.
            await using var verify = NewContext();
            var third = await verify.Items.AsNoTracking().SingleAsync(i => i.Id == f.ThirdItemId);
            Assert.Null(third.SourceSupplierItemId);

            var cat = await verify.PlatformCategories.AsNoTracking().SingleAsync(c => c.Id == f.ThirdCategoryId);
            Assert.Equal("SENTINEL-CAT-644", cat.Name);

            var sup = await verify.Suppliers.AsNoTracking().SingleAsync(s => s.Id == f.ThirdSupplierId);
            Assert.Equal("SENTINEL-SUP-644", sup.Name);

            var orders = await verify.MarketplaceOrders.AsNoTracking()
                .CountAsync(o => o.ClientTenantId == f.ClientTenantId);
            Assert.Equal(0, orders);
        }
        finally
        {
            await CleanupAsync(f.AllTenantIds);
        }
    }

    // ── wiring ───────────────────────────────────────────────────────────────

    private static MarketplaceOrderService BuildOrderService(AppDbContext db)
    {
        var tenantNames = Substitute.For<ISupplierChatRepository>();
        tenantNames.GetTenantDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => "Тест");

        return new MarketplaceOrderService(
            new MarketplaceOrderRepository(db),
            new SupplierAgreementRepository(db),
            // Real ProviderRlsOverride — the SET LOCAL app.role transaction is the point.
            new MarketplaceRepository(db, new ProviderRlsOverride(db)),
            tenantNames,
            Substitute.For<INotificationRepository>(),
            // TASK-645 C1: real TenantSessionOverride, not a mock. NextOrderNumberAsync now counts
            // the supplier's orders under the SUPPLIER tenant's RLS context, so a bare substitute
            // would return a null OrderNumber and the insert would violate the NOT NULL column.
            new TenantSessionOverride(db),
            new ItemRepository(db),
            new ItemService(new ItemRepository(db), new CategoryRepository(db)),
            new LocationRepository(db),
            new UserRepository(db),
            // Phase 3 (plan D4): real repositories — these two only participate in the shipping
            // path, which this class never exercises, but they must resolve against the same
            // AppDbContext so nothing here silently runs on a substitute.
            new TenantRepository(db),
            new SupplierStockRepository(db));
    }

    private static async Task<string?> CurrentRoleAsync(AppDbContext db)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT current_setting('app.role', true)";
        return (string?)await cmd.ExecuteScalarAsync();
    }

    // ── seed / session / cleanup ─────────────────────────────────────────────

    private sealed record Fixture(
        Guid ClientTenantId,
        Guid ClientLocationId,
        Guid ClientUserId,
        Guid SupplierTenantId,
        Guid SupplierId,
        Guid SupplierItemId,
        Guid ThirdTenantId,
        Guid ThirdItemId,
        Guid ThirdCategoryId,
        Guid ThirdSupplierId,
        Guid ClientOwnItemId)
    {
        public Guid[] AllTenantIds => [ClientTenantId, SupplierTenantId, ThirdTenantId];
    }

    private async Task<Fixture> SeedFixtureAsync(bool clientOwnsBarcode = false, bool thirdItemHasGraph = false)
    {
        await using var db = NewContext();

        var clientTenant = Tenant.Create($"Conflicts RLS Client {_run}", $"conflicts-rls-client-{_run}");
        var supplierTenant = Tenant.Create($"Conflicts RLS Supplier {_run}", $"conflicts-rls-supplier-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        var thirdTenant = Tenant.Create($"Conflicts RLS Third {_run}", $"conflicts-rls-third-{_run}");
        db.Tenants.AddRange(clientTenant, supplierTenant, thirdTenant);

        var clientLocation = new Location { TenantId = clientTenant.Id, Name = "Магазин призначення" };
        db.Locations.Add(clientLocation);

        var clientUser = User.Create(
            clientTenant.Id, $"client-{_run}@task644.test", "Клієнт Користувач", "x", "store_manager");
        db.Users.Add(clientUser);

        var supplier = new Supplier { TenantId = supplierTenant.Id, Name = "Постачальник 644" };
        var profile = new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = supplierTenant.Id,
            IsPublic       = true,
            IsOwnerManaged = true,
        };
        var supplierItem = new SupplierItem
        {
            SupplierId  = supplier.Id,
            TenantId    = supplierTenant.Id,
            CustomName  = "Товар постачальника",
            Price       = 12.50m,
            Unit        = "шт",
            IsAvailable = true,
        };
        var supplierBarcode = new SupplierItemBarcode
        {
            SupplierItemId = supplierItem.Id,
            TenantId       = supplierTenant.Id,
            Barcode        = Barcode,
            Kind           = "primary",
        };
        db.Suppliers.Add(supplier);
        db.SupplierProfiles.Add(profile);
        db.SupplierItems.Add(supplierItem);
        db.SupplierItemBarcodes.Add(supplierBarcode);

        db.SupplierAgreements.Add(new SupplierAgreement
        {
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId   = clientTenant.Id,
            Status           = SupplierAgreementStatus.Active,
            ContractNumber   = "ДС-644",
        });

        Guid thirdCategoryId = Guid.Empty;
        Guid thirdSupplierId = Guid.Empty;
        var thirdItem = new Item
        {
            TenantId = thirdTenant.Id,
            Name     = "Чужий товар (третій тенант)",
            Barcodes = [Barcode],
            ImageUrl = "https://example.test/foreign.jpg",
        };
        if (thirdItemHasGraph)
        {
            var thirdCategory = new PlatformCategory { Name = "SENTINEL-CAT-644" };
            var thirdSupplier = new Supplier { TenantId = thirdTenant.Id, Name = "SENTINEL-SUP-644" };
            db.PlatformCategories.Add(thirdCategory);
            db.Suppliers.Add(thirdSupplier);
            thirdCategoryId = thirdCategory.Id;
            thirdSupplierId = thirdSupplier.Id;
            thirdItem.CategoryId = thirdCategory.Id;
            thirdItem.DefaultSupplierId = thirdSupplier.Id;
        }
        db.Items.Add(thirdItem);

        Guid clientOwnItemId = Guid.Empty;
        if (clientOwnsBarcode)
        {
            var clientItem = new Item
            {
                TenantId = clientTenant.Id,
                Name     = "Мій власний товар",
                Barcodes = [Barcode],
                ImageUrl = "https://example.test/own.jpg",
            };
            db.Items.Add(clientItem);
            clientOwnItemId = clientItem.Id;
        }

        await db.SaveChangesAsync();

        return new Fixture(
            clientTenant.Id, clientLocation.Id, clientUser.Id,
            supplierTenant.Id, supplier.Id, supplierItem.Id,
            thirdTenant.Id, thirdItem.Id, thirdCategoryId, thirdSupplierId,
            clientOwnItemId);
    }

    private async Task<RlsSession> OpenSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Exact shape TenantConnectionInterceptor produces for a real staff JWT — Guid-typed,
        // not a raw external string, so EF1002 is a false positive (same reasoning as
        // TenantSessionOverride.ExecuteAsync).
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{tenantId:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new RlsSession(db);
    }

    private async Task CleanupAsync(Guid[] tenantIds)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_order_items WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_orders WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_agreements WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_item_barcodes WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_item_images WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_items WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_metrics WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_profiles WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM items WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM suppliers WHERE \"TenantId\" = ANY({tenantIds})");
        // platform_categories is global now (B1) — no TenantId; the fixture uses a sentinel name.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM platform_categories WHERE \"Name\" = 'SENTINEL-CAT-644'");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM locations WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM users WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = ANY({tenantIds})");
    }

    private AppDbContext NewContext() => new(_options!);

    private sealed class RlsSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public RlsSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
