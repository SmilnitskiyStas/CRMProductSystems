using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-417: live-Postgres, real-repository regression coverage for the critical bug QA found
/// in TASK-416 — <c>POST /api/consumer/loyalty/{tenantId}/join</c> failed 100% of the time with
/// a "new row violates row-level security policy for table customers" 500. Root cause: a
/// consumer session never carries <c>app.tenant_id</c> (see TenantConnectionInterceptor — a
/// ConsumerAccount JWT is cross-tenant by design), and "customers" only has the canonical
/// tenant_isolation/provider_bypass/worker_bypass RLS triad — no identity-based policy the way
/// loyalty_memberships/loyalty_ledger_entries got in TASK-404.
///
/// Unlike <see cref="ShelfGuard.Tests.Auth.LoyaltyServiceTests"/> (NSubstitute-mocked repos —
/// exactly the gap QA's report called out: those tests cannot see real RLS at all), this file
/// builds a real <see cref="AppDbContext"/> against real dev Postgres, switches its connection
/// to a throwaway NOSUPERUSER NOBYPASSRLS role, and sets the exact session-variable shape a
/// real consumer JWT produces (no app.tenant_id, only app.role='consumer' +
/// app.consumer_account_id) — then calls the real <see cref="LoyaltyService.JoinAsync"/>,
/// composed from real CustomerRepository/LoyaltyRepository/ConsumerAccountRepository/
/// TenantRepository/TenantSessionOverride, not stubs, end to end. Same connection/skip/cleanup
/// pattern as LoyaltyRlsIntegrationTests/RlsCrossTenantIntegrationTests.
///
/// TASK-553: part of the <c>TENANT_ISOLATION_TESTS</c> collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class LoyaltyJoinRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    // TASK-417: ONE NpgsqlDataSource (and one DbContextOptions built from it) for this whole
    // test-method instance, reused by every NewContext()/OpenConsumerSessionAsync() call below
    // — each still gets its OWN AppDbContext (and, for the session helpers, its own physical
    // connection via OpenConnectionAsync), but they all share the same cached EF internal
    // service provider. Building a brand-new NpgsqlDataSourceBuilder(...).Build() per call
    // (this file's first version did that, up to a dozen-plus times across just 3 tests) each
    // registers as a distinct DbContextOptions fingerprint, and enough of those accumulated
    // across a full `dotnet test` run to trip EF Core's process-wide
    // ManyServiceProvidersCreatedWarning-as-error threshold — not in this file's own tests, but
    // in two unrelated ones (PosConcurrencySalesIntegrationTests,
    // LoyaltyConcurrencySalesIntegrationTests) that happened to be the ones pushed over the
    // cumulative ~20-instance line. Same root cause LoyaltyRepositoryIntegrationTests.NewContext
    // already flags in its own comment; this file avoids adding to it instead of also
    // downgrading the warning, since one shared data source per test method is both cheaper and
    // closer to how the app itself uses AddDbContext (one options template, many DbContexts).
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public LoyaltyJoinRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _fixture.ConnectionString;

        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping Loyalty join RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
            return;
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
                $"Skipping Loyalty join RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task JoinAsync_new_consumer_creates_customer_and_membership_under_real_rls()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");
        var (consumerAccountId, phone) = await SeedConsumerAccountAsync("Марія Коваленко");

        try
        {
            await using (var session = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var service = BuildLoyaltyService(session.Db);

                var (membership, error, statusCode) = await service.JoinAsync(consumerAccountId, tenant.Id);

                Assert.Null(error);
                Assert.NotNull(membership);
                Assert.Equal(tenant.Id, membership!.TenantId);
                Assert.Equal(0m, membership.Balance);

                // The SET LOCAL override must not leak past its own transaction — check on this
                // SAME still-open connection (not a fresh one, which would trivially never have
                // seen the override in the first place) that app.tenant_id is back to unset now
                // that JoinAsync's internal transaction has committed.
                await using (var checkCmd = session.Db.Database.GetDbConnection().CreateCommand())
                {
                    checkCmd.CommandText = "SELECT current_setting('app.tenant_id', true)";
                    var leaked = (string?)await checkCmd.ExecuteScalarAsync();
                    Assert.True(
                        string.IsNullOrEmpty(leaked),
                        $"Expected app.tenant_id unset on this connection after JoinAsync returned, got '{leaked}'.");
                }
            }

            // Post-hoc verification via a plain, unrestricted context — not part of the RLS
            // path under test, just confirming what actually landed in the database.
            await using var verifyDb = NewContext();
            var customer = await verifyDb.Customers.AsNoTracking()
                .SingleOrDefaultAsync(c => c.TenantId == tenant.Id && c.Phone == phone);
            Assert.NotNull(customer);
            Assert.Equal("Марія Коваленко", customer!.Name);

            var persistedMembership = await verifyDb.LoyaltyMemberships.AsNoTracking()
                .SingleAsync(m => m.TenantId == tenant.Id && m.ConsumerAccountId == consumerAccountId);
            Assert.Equal(customer.Id, persistedMembership.CustomerId);
        }
        finally
        {
            await CleanupTenantDataAsync(tenant.Id);
            await CleanupConsumerAsync(consumerAccountId);
        }
    }

    [Fact]
    public async Task JoinAsync_second_call_is_idempotent_and_does_not_duplicate_customer()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");
        var (consumerAccountId, _) = await SeedConsumerAccountAsync("Ідемпотентний Клієнт");

        try
        {
            await using (var session1 = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var (first, error1, _) = await BuildLoyaltyService(session1.Db).JoinAsync(consumerAccountId, tenant.Id);
                Assert.Null(error1);
                Assert.NotNull(first);
            }

            await using (var session2 = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var (second, error2, _) = await BuildLoyaltyService(session2.Db).JoinAsync(consumerAccountId, tenant.Id);
                Assert.Null(error2);
                Assert.NotNull(second);
            }

            await using var verifyDb = NewContext();
            var customerCount = await verifyDb.Customers.CountAsync(c => c.TenantId == tenant.Id);
            var membershipCount = await verifyDb.LoyaltyMemberships.CountAsync(
                m => m.TenantId == tenant.Id && m.ConsumerAccountId == consumerAccountId);
            Assert.Equal(1, customerCount);
            Assert.Equal(1, membershipCount);
        }
        finally
        {
            await CleanupTenantDataAsync(tenant.Id);
            await CleanupConsumerAsync(consumerAccountId);
        }
    }

    [Fact]
    public async Task JoinAsync_second_tenant_stays_isolated_and_wallet_and_staff_view_still_correct()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenantA = await SeedTenantAsync("loyalty");
        var tenantB = await SeedTenantAsync("loyalty");
        var (consumerAccountId, _) = await SeedConsumerAccountAsync("Крос-Тенант Клієнт");

        try
        {
            await using (var sessionA = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var (membershipA, errorA, _) = await BuildLoyaltyService(sessionA.Db).JoinAsync(consumerAccountId, tenantA.Id);
                Assert.Null(errorA);
                Assert.NotNull(membershipA);
            }

            await using (var sessionB = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var (membershipB, errorB, _) = await BuildLoyaltyService(sessionB.Db).JoinAsync(consumerAccountId, tenantB.Id);
                Assert.Null(errorB);
                Assert.NotNull(membershipB);
            }

            // The cross-tenant wallet read (GET /consumer/loyalty/memberships) is guarded solely
            // by consumer_self_access — untouched by this fix. Confirming it still sees both
            // memberships (and only this consumer's) matters as much as the fix itself.
            await using (var walletSession = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var walletRepo = new LoyaltyRepository(walletSession.Db);
                var wallet = await walletRepo.GetMembershipsForConsumerAsync(consumerAccountId);

                Assert.Equal(2, wallet.Count);
                Assert.Contains(wallet, m => m.TenantId == tenantA.Id);
                Assert.Contains(wallet, m => m.TenantId == tenantB.Id);
            }

            // A staff session scoped to tenant A must see exactly its own customer — no leakage
            // of tenant B's row. Deliberately no C#-side .Where(TenantId) filter here: this
            // proves RLS itself scopes the read, not an application-level filter.
            await using var staffDb = NewContext();
            await staffDb.Database.OpenConnectionAsync();
            await staffDb.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
            // Guid-typed tenantA.Id, not a raw external string — SET doesn't accept bind
            // parameters, so EF1002 is a false positive here (same reasoning as
            // TenantSessionOverride.ExecuteAsync).
#pragma warning disable EF1002
            await staffDb.Database.ExecuteSqlRawAsync(
                $"SET app.tenant_id = '{tenantA.Id:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
            var visibleToStaffA = await staffDb.Customers.CountAsync();
            Assert.Equal(1, visibleToStaffA);
            await staffDb.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
        finally
        {
            await CleanupTenantDataAsync(tenantA.Id);
            await CleanupTenantDataAsync(tenantB.Id);
            await CleanupConsumerAsync(consumerAccountId);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync(params string[] modules)
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"Loyalty Join RLS Test {Guid.NewGuid():N}", $"loyalty-join-rls-test-{Guid.NewGuid():N}");
        if (modules.Length > 0) tenant.UpdateModules(modules);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<(Guid Id, string Phone)> SeedConsumerAccountAsync(string fullName)
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        var phone = $"+380{id:N}"[..13];
        db.ConsumerAccounts.Add(new ConsumerAccount
        {
            Id = id,
            Phone = phone,
            PasswordHash = "x",
            FullName = fullName,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return (id, phone);
    }

    private async Task<ConsumerSession> OpenConsumerSessionAsync(Guid consumerAccountId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Exact shape TenantConnectionInterceptor produces for a real consumer JWT: no
        // app.tenant_id at all, only role + consumer_account_id.
        await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id;");
        // Guid-typed consumerAccountId — see EF1002 justification in ExecuteAsync above.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.role = 'consumer'; SET app.consumer_account_id = '{consumerAccountId:D}';");
#pragma warning restore EF1002
        return new ConsumerSession(db);
    }

    private static LoyaltyService BuildLoyaltyService(AppDbContext db) => new(
        new LoyaltyRepository(db),
        new CustomerRepository(db),
        new TenantRepository(db),
        Substitute.For<IUserRepository>(),
        new ConsumerAccountRepository(db),
        new LocationRepository(db),
        Substitute.For<IPasswordHasher>(),
        BuildTotpStub(),
        Substitute.For<IResolveCodeAttemptTracker>(),
        Substitute.For<IActivityLogRepository>(),
        new TenantSessionOverride(db),
        // TASK-559: JoinAsync itself doesn't consult the consumer-app feature flag (only the
        // controller-level [RequireConsumerFeature("loyalty")] attribute does, tested separately
        // in LoyaltyFeatureGateRlsIntegrationTests) — a default-enabled stub keeps this file's
        // pre-existing JoinAsync-focused tests unaffected.
        BuildFeatureFlagsStub(),
        NullLogger<LoyaltyService>.Instance);

    private static IConsumerFeatureFlagService BuildFeatureFlagsStub()
    {
        var flags = Substitute.For<IConsumerFeatureFlagService>();
        flags.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return flags;
    }

    private static ITotpService BuildTotpStub()
    {
        var totp = Substitute.For<ITotpService>();
        totp.GenerateSecret().Returns(_ => Guid.NewGuid().ToString("N"));
        return totp;
    }

    private async Task CleanupTenantDataAsync(Guid tenantId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM loyalty_memberships WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM customers WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {tenantId}");
    }

    private async Task CleanupConsumerAsync(Guid consumerAccountId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM consumer_accounts WHERE \"Id\" = {consumerAccountId}");
    }

    /// <summary>
    /// Builds a fresh AppDbContext from the ONE shared _options/_dataSource for this test
    /// method (see the field comments above) — cheap, and does not register as yet another
    /// distinct EF internal service provider the way a brand-new NpgsqlDataSourceBuilder(...)
    /// per call would.
    /// </summary>
    private AppDbContext NewContext() => new(_options!);

    /// <summary>
    /// Wraps a manually-opened, role-switched AppDbContext connection so callers can
    /// `await using` it and get RESET ROLE on disposal. This IS load-bearing now that every
    /// context in this test method shares one NpgsqlDataSource/pool: without resetting ROLE
    /// before the connection returns to the pool, a later NewContext()/OpenConsumerSessionAsync
    /// call in the same test could check out this same physical connection still running as the
    /// throwaway RLS role. Every session-opening helper here also unconditionally re-asserts
    /// the full app.tenant_id/app.role/app.consumer_account_id shape it needs at the start
    /// regardless of what a reused pooled connection had left over, so this is belt-and-
    /// suspenders rather than the only thing preventing cross-test leakage.
    /// </summary>
    private sealed class ConsumerSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public ConsumerSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
