using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-405 (Loyalty Фаза 0): live-Postgres coverage for
/// <see cref="LoyaltyRepository.TryClaimTimestepAsync"/> — the anti-replay guard behind
/// POST /api/loyalty/resolve-code. This is the one piece of new code in this task that a
/// mocked/NSubstitute unit test structurally cannot verify: the actual raw
/// ExecuteSqlInterpolatedAsync UPDATE statement (column names, quoting, WHERE-guard
/// semantics) only proves itself correct against a real Postgres connection. Same
/// connection/skip pattern as PosConcurrencySalesIntegrationTests — no RLS session
/// variables are set here (this repo's dev connection is not RLS-restricted, same as that
/// file), since the scenario under test is the atomic UPDATE guard, not tenant isolation
/// (already covered by LoyaltyRlsIntegrationTests).
/// </summary>
public sealed class LoyaltyRepositoryIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _membershipId;
    private Guid _consumerAccountId;

    public LoyaltyRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping LoyaltyRepository integration test — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Loyalty Repo Test {Guid.NewGuid():N}", $"loyalty-repo-test-{Guid.NewGuid():N}");
        var consumerAccountId = Guid.NewGuid();
        var consumer = new ConsumerAccount
        {
            Id = consumerAccountId,
            Phone = $"+380{consumerAccountId:N}"[..13],
            PasswordHash = "x",
            FullName = "Loyalty Repo Test Consumer",
        };
        var membership = new LoyaltyMembership
        {
            TenantId = tenant.Id,
            ConsumerAccountId = consumerAccountId,
            TotpSecret = "SECRET",
            Balance = 0m,
        };

        db.Tenants.Add(tenant);
        db.ConsumerAccounts.Add(consumer);
        db.LoyaltyMemberships.Add(membership);
        await db.SaveChangesAsync();

        _tenantId = tenant.Id;
        _membershipId = membership.Id;
        _consumerAccountId = consumerAccountId;
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM loyalty_memberships WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM consumer_accounts WHERE \"Id\" = {_consumerAccountId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task TryClaimTimestepAsync_first_claim_succeeds_and_persists()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new LoyaltyRepository(db);

        var claimed = await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 100L);

        Assert.True(claimed);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.LoyaltyMemberships.AsNoTracking().SingleAsync(m => m.Id == _membershipId);
        Assert.Equal(100L, persisted.LastRedeemedTimestep);
    }

    [Fact]
    public async Task TryClaimTimestepAsync_replay_of_same_or_earlier_timestep_fails()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new LoyaltyRepository(db);

        Assert.True(await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 200L));

        // Same timestep replayed (e.g. a screenshot scanned twice within the TOTP window).
        Assert.False(await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 200L));

        // An earlier timestep than the high-water mark must also be rejected.
        Assert.False(await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 199L));
    }

    [Fact]
    public async Task TryClaimTimestepAsync_later_timestep_after_rotation_succeeds()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new LoyaltyRepository(db);

        Assert.True(await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 300L));
        // The QR code rotated (new TOTP timestep) — a later value must be claimable.
        Assert.True(await repo.TryClaimTimestepAsync(_membershipId, _tenantId, 301L));
    }

    [Fact]
    public async Task TryClaimTimestepAsync_wrong_tenant_scope_fails_without_mutating()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new LoyaltyRepository(db);

        var claimed = await repo.TryClaimTimestepAsync(_membershipId, Guid.NewGuid(), 400L);

        Assert.False(claimed);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.LoyaltyMemberships.AsNoTracking().SingleAsync(m => m.Id == _membershipId);
        Assert.Null(persisted.LastRedeemedTimestep);
    }

    // KI-035: this used to build (and never dispose) a brand-new NpgsqlDataSource on EVERY call —
    // each one stranding a physical Postgres backend for the rest of the run. Now one shared,
    // process-wide pool (see TestPostgres).
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
