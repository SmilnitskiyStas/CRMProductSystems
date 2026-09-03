using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Users;
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
/// TASK-644 / KI-036 (ADR-035) — live-Postgres coverage for the <see cref="ProviderRlsOverride"/>
/// primitive that replaced <c>MarketplaceRepository.SetProviderRoleAsync</c>, plus the two
/// legitimate cross-tenant writes that used to piggy-back on the leaked provider role and now go
/// through composite repository methods:
///   • <b>W1</b> — <see cref="MarketplaceService.CreateReviewAsync"/> → <c>UpsertMetricsRatingAsync</c>
///     (both the INSERT branch — first-ever review for a supplier — and the UPDATE branch).
///   • <b>W2</b> — <see cref="SupplierCabinetService.ReplyToReviewAsync"/> → <c>SetReviewReplyAsync</c>.
///
/// Every method runs under a real <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS) session with
/// the exact <c>app.tenant_id</c>/<c>app.role</c> shape a real JWT produces. The key property under
/// test is that <c>SET LOCAL app.role = 'provider'</c> is confined to one transaction and reverts
/// the instant it commits — checked on the same still-open connection immediately afterwards. Same
/// harness / collection / skip conventions as
/// <see cref="SupplierAgreementMarkSignedRlsIntegrationTests"/>.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MarketplaceProviderBypassScopeRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");

    public MarketplaceProviderBypassScopeRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping marketplace provider-bypass-scope RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
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
                $"Skipping marketplace provider-bypass-scope RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── The primitive: bypass works, then reverts ────────────────────────────

    [Fact]
    public async Task GetSupplierItemsAsync_under_a_client_session_reads_cross_tenant_then_reverts_the_role()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var s = await SeedSupplierAsync(reviewerCount: 0, withImage: false, foreignItems: 2, clientOwnItems: 1);
        try
        {
            await using var session = await OpenSessionAsync(s.ClientTenantId);
            var repo = new MarketplaceRepository(session.Db, new ProviderRlsOverride(session.Db));

            var items = await repo.GetSupplierItemsAsync(s.SupplierId);

            // Bypass works — the client session reads the foreign supplier's catalog.
            Assert.Contains(items, i => i.Id == s.SupplierItemId);

            // …and the provider role did not survive the call.
            Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));

            // A plain items read on the same session now sees ONLY this tenant's rows — if the
            // role had leaked, this would also count the supplier tenant's 2 rows.
            var visible = await session.Db.Items.CountAsync();
            Assert.Equal(1, visible);
        }
        finally
        {
            await CleanupAsync(s.AllTenantIds);
        }
    }

    [Fact]
    public async Task Raw_set_local_app_role_provider_reverts_when_its_transaction_commits()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenantId = await SeedBareTenantAsync();
        try
        {
            await using var session = await OpenSessionAsync(tenantId);

            await using (var setCmd = session.Db.Database.GetDbConnection().CreateCommand())
            {
                setCmd.CommandText = "BEGIN; SET LOCAL app.role = 'provider'; COMMIT;";
                await setCmd.ExecuteNonQueryAsync();
            }

            Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
        }
        finally
        {
            await CleanupAsync([tenantId]);
        }
    }

    // ── W1: cross-tenant supplier_metrics write (INSERT + UPDATE branches) ────

    [Fact]
    public async Task CreateReviewAsync_first_ever_review_inserts_the_cross_tenant_supplier_metrics_row()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var s = await SeedSupplierAsync(reviewerCount: 1);
        var reviewer = s.ReviewerTenantIds[0];
        try
        {
            await using (var session = await OpenSessionAsync(reviewer))
            {
                var service = new MarketplaceService(
                    new MarketplaceRepository(session.Db, new ProviderRlsOverride(session.Db)),
                    new LocationRepository(session.Db),
                    new CategoryRepository(session.Db));

                var (review, error, isDuplicate) = await service.CreateReviewAsync(
                    s.SupplierId, reviewer, new SupplierReviewCreateDto(5, "Чудовий постачальник"));

                Assert.Null(error);
                Assert.False(isDuplicate);
                Assert.NotNull(review);
                Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
            }

            await using var verify = NewContext();
            var persistedReview = await verify.SupplierReviews.AsNoTracking()
                .SingleAsync(r => r.SupplierId == s.SupplierId && r.TenantId == reviewer);
            Assert.Equal((short)5, persistedReview.Rating);

            // INSERT branch: the row is created and owned by the SUPPLIER tenant, not the reviewer.
            var metrics = await verify.SupplierMetrics.AsNoTracking()
                .SingleAsync(m => m.SupplierId == s.SupplierId);
            Assert.Equal(s.SupplierTenantId, metrics.TenantId);
            Assert.Equal(5.00m, metrics.Rating);
        }
        finally
        {
            await CleanupAsync(s.AllTenantIds);
        }
    }

    [Fact]
    public async Task CreateReviewAsync_second_review_from_another_tenant_updates_the_existing_metrics_row()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var s = await SeedSupplierAsync(reviewerCount: 2);
        var reviewer1 = s.ReviewerTenantIds[0];
        var reviewer2 = s.ReviewerTenantIds[1];
        try
        {
            await using (var session1 = await OpenSessionAsync(reviewer1))
            {
                var service = new MarketplaceService(
                    new MarketplaceRepository(session1.Db, new ProviderRlsOverride(session1.Db)),
                    new LocationRepository(session1.Db),
                    new CategoryRepository(session1.Db));
                var (_, error, _) = await service.CreateReviewAsync(
                    s.SupplierId, reviewer1, new SupplierReviewCreateDto(4, "Добре"));
                Assert.Null(error);
            }

            await using (var verifyInsert = NewContext())
            {
                var m = await verifyInsert.SupplierMetrics.AsNoTracking().SingleAsync(x => x.SupplierId == s.SupplierId);
                Assert.Equal(4.00m, m.Rating);
            }

            await using (var session2 = await OpenSessionAsync(reviewer2))
            {
                var service = new MarketplaceService(
                    new MarketplaceRepository(session2.Db, new ProviderRlsOverride(session2.Db)),
                    new LocationRepository(session2.Db),
                    new CategoryRepository(session2.Db));
                var (_, error, _) = await service.CreateReviewAsync(
                    s.SupplierId, reviewer2, new SupplierReviewCreateDto(2, "Погано"));
                Assert.Null(error);
                Assert.Equal("store_manager", await CurrentRoleAsync(session2.Db));
            }

            await using var verify = NewContext();
            // UPDATE branch: still exactly one metrics row, rating recalculated over both reviews.
            var metrics = await verify.SupplierMetrics.AsNoTracking()
                .SingleAsync(m => m.SupplierId == s.SupplierId);
            Assert.Equal(s.SupplierTenantId, metrics.TenantId);
            Assert.Equal(3.00m, metrics.Rating); // avg(4, 2)
            Assert.Equal(2, await verify.SupplierReviews.AsNoTracking().CountAsync(r => r.SupplierId == s.SupplierId));
        }
        finally
        {
            await CleanupAsync(s.AllTenantIds);
        }
    }

    // ── W2: cross-tenant supplier_reviews reply write ────────────────────────

    [Fact]
    public async Task ReplyToReviewAsync_under_the_supplier_session_persists_a_reply_on_a_foreign_tenant_review()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var s = await SeedSupplierAsync(reviewerCount: 1, ownerManaged: true);
        var reviewer = s.ReviewerTenantIds[0];
        var reviewId = await SeedReviewAsync(s.SupplierId, reviewer, rating: 3, comment: "Середньо");
        try
        {
            await using (var session = await OpenSessionAsync(s.SupplierTenantId))
            {
                var cabinet = new SupplierCabinetService(
                    new MarketplaceRepository(session.Db, new ProviderRlsOverride(session.Db)),
                    Substitute.For<IMarketplaceService>(),
                    Substitute.For<IUserService>(),
                    Substitute.For<IUserRepository>(),
                    Substitute.For<ISupplierRolesRepository>(),
                    Substitute.For<ISupplierTaskRepository>());

                var (reply, error) = await cabinet.ReplyToReviewAsync(
                    s.SupplierTenantId, reviewId, "Дякуємо за ваш відгук!");

                Assert.Null(error);
                Assert.NotNull(reply);
                Assert.Equal("Дякуємо за ваш відгук!", reply!.ReplyText);
                Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
            }

            await using var verify = NewContext();
            var persisted = await verify.SupplierReviews.AsNoTracking().SingleAsync(r => r.Id == reviewId);
            Assert.Equal("Дякуємо за ваш відгук!", persisted.ReplyText);
            Assert.NotNull(persisted.RepliedAt);
            Assert.Equal(reviewer, persisted.TenantId); // row still owned by the reviewer tenant
        }
        finally
        {
            await CleanupAsync(s.AllTenantIds);
        }
    }

    // ── Positive control: public marketplace reads stay cross-tenant ─────────

    [Fact]
    public async Task Public_marketplace_reads_still_cross_tenant_under_a_client_session()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var s = await SeedSupplierAsync(reviewerCount: 0, withImage: true);
        try
        {
            await using var session = await OpenSessionAsync(s.ClientTenantId);
            var repo = new MarketplaceRepository(session.Db, new ProviderRlsOverride(session.Db));

            var byId = await repo.GetSupplierByIdAsync(s.SupplierId);
            Assert.NotNull(byId);
            Assert.Equal(s.SupplierTenantId, byId!.Value.Supplier.TenantId);

            var listed = await repo.GetPublicSuppliersAsync(null, null, null, 1, 200);
            Assert.Contains(listed, r => r.Supplier.Id == s.SupplierId);

            var images = await repo.GetSupplierItemImagesByIdsAsync([s.SupplierItemId]);
            Assert.True(images.ContainsKey(s.SupplierItemId));
            Assert.Equal($"https://example.test/img-{_run}.jpg", images[s.SupplierItemId][0].Url);

            Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
        }
        finally
        {
            await CleanupAsync(s.AllTenantIds);
        }
    }

    // ── TASK-645 C1: order numbering must not depend on the leaked provider role ──

    /// <summary>
    /// <c>MP-{yyyy}-{NNN}</c> is documented as sequential <b>per supplier</b>, and
    /// <c>NextOrderNumberAsync</c> derives NNN from <c>CountForSupplierAsync</c>. That count runs
    /// on the CLIENT session, after the provider-bypass reads — so before TASK-643 it silently
    /// relied on the leaked <c>app.role='provider'</c> to see all of the supplier's orders.
    ///
    /// Once the leak was scoped, <c>marketplace_orders</c>' OR-based tenant_isolation
    /// (<c>SupplierTenantId = session OR ClientTenantId = session</c>) narrowed the count to the
    /// orders the calling client is a party to, restarting the sequence per client: two different
    /// clients of one supplier would BOTH be issued <c>MP-{yyyy}-001</c>. There is no unique index
    /// on OrderNumber, so it would corrupt silently.
    ///
    /// Only a real database can catch this — both the unit tests and the other RLS file mock or
    /// seed a single client. TASK-645 C1 fixes it by counting under the SUPPLIER tenant's context
    /// via ITenantSessionOverride; this test pins that.
    /// </summary>
    [Fact]
    public async Task Order_numbers_stay_sequential_per_supplier_across_two_different_client_tenants()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedTwoClientOrderFixtureAsync();
        try
        {
            var first = await PlaceOrderAsync(f, f.ClientATenantId, f.ClientALocationId, f.ClientAUserId);
            var second = await PlaceOrderAsync(f, f.ClientBTenantId, f.ClientBLocationId, f.ClientBUserId);

            // The regression: pre-C1 both of these are "MP-{year}-001".
            Assert.NotEqual(first, second);

            var year = DateTime.UtcNow.Year;
            Assert.Equal($"MP-{year}-001", first);
            Assert.Equal($"MP-{year}-002", second);

            await using var verify = NewContext();
            var numbers = await verify.MarketplaceOrders.AsNoTracking()
                .Where(o => o.SupplierTenantId == f.SupplierTenantId)
                .Select(o => o.OrderNumber)
                .ToListAsync();
            Assert.Equal(2, numbers.Count);
            Assert.Equal(2, numbers.Distinct().Count());
        }
        finally
        {
            await CleanupAsync(f.AllTenantIds);
        }
    }

    private async Task<string> PlaceOrderAsync(TwoClientOrderFixture f, Guid clientTenantId, Guid locationId, Guid userId)
    {
        await using var session = await OpenSessionAsync(clientTenantId);
        var service = BuildOrderService(session.Db);

        var (dto, error, _) = await service.CreateOrderAsync(
            clientTenantId, f.SupplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(f.SupplierItemId, 1)],
                Comment: null,
                DestinationStoreId: locationId),
            userId);

        Assert.Null(error);
        Assert.NotNull(dto);

        // The supplier-scoped count must not leave the provider role — or any other tenant's
        // app.tenant_id — behind on this connection.
        Assert.Equal("store_manager", await CurrentRoleAsync(session.Db));
        return dto!.OrderNumber;
    }

    private static MarketplaceOrderService BuildOrderService(AppDbContext db)
    {
        var tenantNames = Substitute.For<ISupplierChatRepository>();
        tenantNames.GetTenantDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => "Тест");

        return new MarketplaceOrderService(
            new MarketplaceOrderRepository(db),
            new SupplierAgreementRepository(db),
            new MarketplaceRepository(db, new ProviderRlsOverride(db)),
            tenantNames,
            Substitute.For<INotificationRepository>(),
            // Real override — the supplier-scoped order-number count is exactly what's under test.
            new TenantSessionOverride(db),
            new ItemRepository(db),
            new ItemService(new ItemRepository(db), new CategoryRepository(db)),
            new LocationRepository(db),
            new UserRepository(db),
            // Phase 3 (plan D4) — unused by this class's order-creation scenarios, but wired to
            // the same AppDbContext so nothing runs on a substitute.
            new TenantRepository(db),
            new SupplierStockRepository(db));
    }

    private sealed record TwoClientOrderFixture(
        Guid ClientATenantId, Guid ClientALocationId, Guid ClientAUserId,
        Guid ClientBTenantId, Guid ClientBLocationId, Guid ClientBUserId,
        Guid SupplierTenantId, Guid SupplierId, Guid SupplierItemId)
    {
        public Guid[] AllTenantIds => [ClientATenantId, ClientBTenantId, SupplierTenantId];
    }

    private async Task<TwoClientOrderFixture> SeedTwoClientOrderFixtureAsync()
    {
        await using var db = NewContext();

        var supplierTenant = Tenant.Create($"Seq RLS Supplier {_run}", $"seq-rls-supplier-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        var clientA = Tenant.Create($"Seq RLS Client A {_run}", $"seq-rls-client-a-{_run}");
        var clientB = Tenant.Create($"Seq RLS Client B {_run}", $"seq-rls-client-b-{_run}");
        db.Tenants.AddRange(supplierTenant, clientA, clientB);

        var locationA = new Location { TenantId = clientA.Id, Name = "Магазин A" };
        var locationB = new Location { TenantId = clientB.Id, Name = "Магазин B" };
        db.Locations.AddRange(locationA, locationB);

        var userA = User.Create(clientA.Id, $"seq-a-{_run}@task645.test", "Клієнт A", "x", "store_manager");
        var userB = User.Create(clientB.Id, $"seq-b-{_run}@task645.test", "Клієнт B", "x", "store_manager");
        db.Users.AddRange(userA, userB);

        var supplier = new Supplier { TenantId = supplierTenant.Id, Name = $"Постачальник seq {_run}" };
        db.Suppliers.Add(supplier);
        db.SupplierProfiles.Add(new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = supplierTenant.Id,
            IsPublic       = true,
            IsOwnerManaged = true,
        });

        // No barcodes — CreateOrderAsync's collision check short-circuits and every line simply
        // auto-provisions a new Item in the ordering client's own catalog.
        var supplierItem = new SupplierItem
        {
            SupplierId  = supplier.Id,
            TenantId    = supplierTenant.Id,
            CustomName  = "Позиція для нумерації",
            Price       = 5.00m,
            Unit        = "шт",
            IsAvailable = true,
        };
        db.SupplierItems.Add(supplierItem);

        foreach (var clientId in new[] { clientA.Id, clientB.Id })
        {
            db.SupplierAgreements.Add(new SupplierAgreement
            {
                SupplierTenantId = supplierTenant.Id,
                ClientTenantId   = clientId,
                Status           = SupplierAgreementStatus.Active,
                ContractNumber   = $"ДС-645-{clientId:N}"[..20],
            });
        }

        await db.SaveChangesAsync();

        return new TwoClientOrderFixture(
            clientA.Id, locationA.Id, userA.Id,
            clientB.Id, locationB.Id, userB.Id,
            supplierTenant.Id, supplier.Id, supplierItem.Id);
    }

    // ── seed / session / cleanup ─────────────────────────────────────────────

    private sealed record SupplierFixture(
        Guid ClientTenantId,
        Guid SupplierTenantId,
        Guid SupplierId,
        Guid SupplierItemId,
        IReadOnlyList<Guid> ReviewerTenantIds)
    {
        public Guid[] AllTenantIds => new[] { ClientTenantId, SupplierTenantId }.Concat(ReviewerTenantIds).ToArray();
    }

    private async Task<SupplierFixture> SeedSupplierAsync(
        int reviewerCount,
        bool withImage = false,
        bool ownerManaged = false,
        int foreignItems = 0,
        int clientOwnItems = 0)
    {
        await using var db = NewContext();

        var clientTenant = Tenant.Create($"Bypass RLS Client {_run}", $"bypass-rls-client-{_run}");
        var supplierTenant = Tenant.Create($"Bypass RLS Supplier {_run}", $"bypass-rls-supplier-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        db.Tenants.AddRange(clientTenant, supplierTenant);

        var reviewerIds = new List<Guid>();
        for (var i = 0; i < reviewerCount; i++)
        {
            var t = Tenant.Create($"Bypass RLS Reviewer {i} {_run}", $"bypass-rls-reviewer-{i}-{_run}");
            db.Tenants.Add(t);
            reviewerIds.Add(t.Id);
        }

        var supplier = new Supplier { TenantId = supplierTenant.Id, Name = $"Постачальник {_run}" };
        var profile = new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = supplierTenant.Id,
            IsPublic       = true,
            IsOwnerManaged = ownerManaged,
        };
        var supplierItem = new SupplierItem
        {
            SupplierId  = supplier.Id,
            TenantId    = supplierTenant.Id,
            CustomName  = "Каталожна позиція",
            Price       = 9.99m,
            Unit        = "шт",
            IsAvailable = true,
        };
        db.Suppliers.Add(supplier);
        db.SupplierProfiles.Add(profile);
        db.SupplierItems.Add(supplierItem);

        if (withImage)
        {
            db.SupplierItemImages.Add(new SupplierItemImage
            {
                SupplierItemId = supplierItem.Id,
                TenantId       = supplierTenant.Id,
                Url            = $"https://example.test/img-{_run}.jpg",
                Kind           = "main",
                SortOrder      = 0,
            });
        }

        for (var i = 0; i < foreignItems; i++)
            db.Items.Add(new Item { TenantId = supplierTenant.Id, Name = $"Товар постачальника {i} {_run}" });
        for (var i = 0; i < clientOwnItems; i++)
            db.Items.Add(new Item { TenantId = clientTenant.Id, Name = $"Товар клієнта {i} {_run}" });

        await db.SaveChangesAsync();

        return new SupplierFixture(clientTenant.Id, supplierTenant.Id, supplier.Id, supplierItem.Id, reviewerIds);
    }

    private async Task<Guid> SeedReviewAsync(Guid supplierId, Guid reviewerTenantId, short rating, string comment)
    {
        await using var db = NewContext();
        var review = new SupplierReview
        {
            SupplierId = supplierId,
            TenantId   = reviewerTenantId,
            Rating     = rating,
            Comment    = comment,
        };
        db.SupplierReviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    private async Task<Guid> SeedBareTenantAsync()
    {
        await using var db = NewContext();
        var t = Tenant.Create($"Bypass RLS Bare {_run}", $"bypass-rls-bare-{_run}");
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static async Task<string?> CurrentRoleAsync(AppDbContext db)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT current_setting('app.role', true)";
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<RlsSession> OpenSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{tenantId:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new RlsSession(db);
    }

    private async Task CleanupAsync(Guid[] tenantIds)
    {
        await using var db = NewContext();
        // TASK-645 C1's fixture is the only one that creates these; FK order matters
        // (order items → orders → agreements, and orders reference locations/users below).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_order_items WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_orders WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_agreements WHERE \"ClientTenantId\" = ANY({tenantIds}) OR \"SupplierTenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_reviews WHERE \"TenantId\" = ANY({tenantIds}) OR \"SupplierId\" IN (SELECT \"Id\" FROM suppliers WHERE \"TenantId\" = ANY({tenantIds}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_metrics WHERE \"TenantId\" = ANY({tenantIds}) OR \"SupplierId\" IN (SELECT \"Id\" FROM suppliers WHERE \"TenantId\" = ANY({tenantIds}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_item_images WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_item_barcodes WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_items WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_profiles WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM suppliers WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM items WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM users WHERE \"TenantId\" = ANY({tenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM locations WHERE \"TenantId\" = ANY({tenantIds})");
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
