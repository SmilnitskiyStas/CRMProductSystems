using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Vchasno;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-582: live-Postgres regression coverage for the cross-tenant RLS violation in
/// <see cref="SupplierAgreementService.MarkSignedAsync"/> — <c>POST
/// /api/supplier-cabinet/cooperation-requests/{id}/mark-signed</c> 500'd (masked as a CORS
/// error in the browser, since the API had no exception-handling middleware and the connection
/// aborted before headers were sent) every time a supplier confirmed a signed agreement.
/// Root cause: <c>EnqueueSignedNotificationAsync</c> inserts into <c>notification_queue</c> with
/// <c>TenantId = agreement.ClientTenantId</c> (the notification recipient) while the DB session
/// is authenticated as the calling SUPPLIER tenant (<c>app.tenant_id</c> from the JWT) —
/// notification_queue's <c>tenant_isolation</c> RLS policy only allows
/// <c>TenantId = session tenant OR NULL</c>, so the raw insert throws PostgresException 42501.
///
/// Same real-Postgres/real-repository/throwaway-NOSUPERUSER-role pattern as
/// <see cref="LoyaltyJoinRlsIntegrationTests"/> (TASK-417) — this file drives the real
/// <see cref="SupplierAgreementService"/> end to end (not NSubstitute-mocked repos), so it
/// actually exercises the fix's <c>ITenantSessionOverride</c> wrapping under genuine RLS
/// enforcement, not just a pass-through test double.
///
/// TASK-553: part of the TENANT_ISOLATION_TESTS collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class SupplierAgreementMarkSignedRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public SupplierAgreementMarkSignedRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping MarkSignedAsync RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
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
                $"Skipping MarkSignedAsync RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task MarkSignedAsync_under_real_supplier_rls_session_enqueues_client_notification_without_throwing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var supplierTenant = await SeedTenantAsync();
        var clientTenant = await SeedTenantAsync();
        var agreementId = await SeedAwaitingSignatureAgreementAsync(supplierTenant.Id, clientTenant.Id);

        try
        {
            await using (var session = await OpenSupplierSessionAsync(supplierTenant.Id))
            {
                var service = BuildAgreementService(session.Db);

                var (dto, error) = await service.MarkSignedAsync(supplierTenant.Id, agreementId);

                Assert.Null(error);
                Assert.NotNull(dto);
                Assert.Equal(SupplierAgreementStatus.Active, dto!.Status);

                // The SET LOCAL override inside ITenantSessionOverride must not leak past its own
                // transaction — check on this SAME still-open connection that app.tenant_id is
                // back to the supplier tenant (what this session started with), not the client
                // tenant the override temporarily assumed.
                await using var checkCmd = session.Db.Database.GetDbConnection().CreateCommand();
                checkCmd.CommandText = "SELECT current_setting('app.tenant_id', true)";
                var current = (string?)await checkCmd.ExecuteScalarAsync();
                Assert.Equal(supplierTenant.Id.ToString(), current);
            }

            // Post-hoc verification via a plain, unrestricted context — not part of the RLS path
            // under test, just confirming what actually landed in the database.
            await using var verifyDb = NewContext();
            var agreement = await verifyDb.SupplierAgreements.AsNoTracking()
                .SingleAsync(a => a.Id == agreementId);
            Assert.Equal(SupplierAgreementStatus.Active, agreement.Status);
            Assert.NotNull(agreement.SignedAt);

            var notification = await verifyDb.NotificationQueues.AsNoTracking()
                .SingleOrDefaultAsync(n => n.EventType == "supplier_agreement.signed" && n.TenantId == clientTenant.Id);
            Assert.NotNull(notification);
            Assert.Equal("pending", notification!.Status);
            Assert.Equal("system", notification.Channel);
        }
        finally
        {
            await CleanupAsync(supplierTenant.Id, clientTenant.Id);
        }
    }

    [Fact]
    public async Task Direct_insert_of_client_tenant_notification_under_supplier_session_throws_rls_violation_without_override()
    {
        // Negative control: proves the ROOT CAUSE this fix addresses is real RLS enforcement, not
        // a hypothetical — a raw insert shaped exactly like the pre-fix EnqueueSignedNotificationAsync
        // call (TenantId = a DIFFERENT tenant than the session's app.tenant_id) is rejected by
        // notification_queue's tenant_isolation WITH CHECK clause when no
        // ITenantSessionOverride is involved. This is what MarkSignedAsync used to hit directly.
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var supplierTenant = await SeedTenantAsync();
        var clientTenant = await SeedTenantAsync();

        try
        {
            await using var session = await OpenSupplierSessionAsync(supplierTenant.Id);

            session.Db.NotificationQueues.Add(new NotificationQueue
            {
                TenantId  = clientTenant.Id,
                Title     = "TASK-582 negative control",
                Channel   = "system",
                EventType = "supplier_agreement.signed",
                Status    = "pending",
            });

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => session.Db.SaveChangesAsync());
            Assert.Contains("42501", ex.ToString());
        }
        finally
        {
            await CleanupAsync(supplierTenant.Id, clientTenant.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"Mark Signed RLS Test {Guid.NewGuid():N}", $"mark-signed-rls-test-{Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<Guid> SeedAwaitingSignatureAgreementAsync(Guid supplierTenantId, Guid clientTenantId)
    {
        await using var db = NewContext();
        var agreement = new SupplierAgreement
        {
            SupplierTenantId = supplierTenantId,
            ClientTenantId   = clientTenantId,
            Status           = SupplierAgreementStatus.AwaitingSignature,
            ContractNumber   = "ДС-TEST-001",
        };
        db.SupplierAgreements.Add(agreement);
        await db.SaveChangesAsync();
        return agreement.Id;
    }

    private async Task<SupplierSession> OpenSupplierSessionAsync(Guid supplierTenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Exact shape TenantConnectionInterceptor produces for a real staff JWT at the supplier
        // tenant (the caller of POST /api/supplier-cabinet/.../mark-signed). Guid-typed
        // supplierTenantId, not a raw external string — SET doesn't accept bind parameters, so
        // EF1002 is a false positive here (same reasoning as TenantSessionOverride.ExecuteAsync).
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{supplierTenantId:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new SupplierSession(db);
    }

    private static SupplierAgreementService BuildAgreementService(AppDbContext db) => new(
        new SupplierAgreementRepository(db),
        new SupplierContractSettingsRepository(db),
        // TASK-643: real ProviderRlsOverride, not a pass-through — this suite runs against live
        // Postgres and the repository's cross-tenant reads must exercise the genuine
        // SET LOCAL app.role transaction here.
        new MarketplaceRepository(db, new ProviderRlsOverride(db)),
        new SupplierChatRepository(db),
        Substitute.For<IContractPdfGenerator>(),
        Substitute.For<IVchasnoClientFactory>(),
        Substitute.For<ILegalEntityService>(),
        new NotificationRepository(db),
        new TenantSessionOverride(db));

    private async Task CleanupAsync(Guid supplierTenantId, Guid clientTenantId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM notification_queue WHERE \"TenantId\" IN ({clientTenantId}, {supplierTenantId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_agreements WHERE \"SupplierTenantId\" = {supplierTenantId} OR \"ClientTenantId\" = {clientTenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" IN ({supplierTenantId}, {clientTenantId})");
    }

    private AppDbContext NewContext() => new(_options!);

    /// <summary>
    /// Wraps a manually-opened, role-switched AppDbContext connection so callers can `await
    /// using` it and get RESET ROLE on disposal — same convention as LoyaltyJoinRlsIntegrationTests'
    /// ConsumerSession, needed because every context in this test method shares one
    /// NpgsqlDataSource/pool.
    /// </summary>
    private sealed class SupplierSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public SupplierSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
