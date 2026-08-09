using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class LoyaltyServiceTests
{
    private readonly ILoyaltyRepository _loyalty = Substitute.For<ILoyaltyRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IConsumerAccountRepository _consumerAccounts = Substitute.For<IConsumerAccountRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IResolveCodeAttemptTracker _attempts = Substitute.For<IResolveCodeAttemptTracker>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITenantSessionOverride _tenantScope = Substitute.For<ITenantSessionOverride>();
    private readonly LoyaltyService _sut;

    public LoyaltyServiceTests()
    {
        _sut = new LoyaltyService(
            _loyalty, _customers, _tenants, _users, _consumerAccounts, _hasher, _totp, _attempts,
            _activityLogs, _tenantScope, NullLogger<LoyaltyService>.Instance);

        _customers.CreateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<Customer>()));

        // TASK-417: JoinAsync's create-branch now runs inside _tenantScope.ExecuteAsync — a
        // pure pass-through here keeps every pre-existing JoinAsync test (and its Arg.Is<>
        // assertions against the repos called from inside that delegate) working unchanged,
        // since this mock just invokes the delegate immediately instead of actually opening a
        // transaction (that real behavior is covered live by LoyaltyJoinRlsIntegrationTests).
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<LoyaltyMembership>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<LoyaltyMembership>>>()());
    }

    private static Tenant MakeTenant(params string[] modules)
    {
        var tenant = Tenant.Create("Acme", "acme");
        if (modules.Length > 0) tenant.UpdateModules(modules);
        return tenant;
    }

    private static User MakeUserWithPhone(string? phone)
    {
        var user = User.Create(Guid.NewGuid(), "staff@example.com", "Staff Person", "hash", "cashier");
        user.UpdateProfile(user.FullName, phone);
        return user;
    }

    // ── JoinAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinAsync_unknown_consumer_returns_404()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).ReturnsNull();

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, Guid.NewGuid());

        Assert.Null(membership);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task JoinAsync_unknown_tenant_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).ReturnsNull();

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(membership);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task JoinAsync_module_not_active_returns_403()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant()); // no modules

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(membership);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public async Task JoinAsync_existing_membership_is_idempotent()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default)
            .Returns(new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, Balance = 42m });

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(42m, membership.Balance);
        await _loyalty.DidNotReceive().AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    [Fact]
    public async Task JoinAsync_new_member_creates_customer_and_membership()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "Ірина Петренко", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _totp.GenerateSecret().Returns("SECRET");

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(0m, membership.Balance);
        await _customers.Received(1).CreateAsync(
            Arg.Is<Customer>(c => c.Phone == "+380501234567" && c.Tags.Contains("loyalty")), default);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m =>
                m.TenantId == tenantId && m.ConsumerAccountId == consumerId && m.TotpSecret == "SECRET"),
            default);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    /// <summary>
    /// TASK-417 regression pin: the customer-lookup-or-create + membership-create branch must
    /// go through ITenantSessionOverride with the join's own tenantId, not run as a bare
    /// ambient-session call — that ambient session (a consumer JWT) never carries app.tenant_id
    /// at all, which is exactly what made "customers" reject every insert (500, RLS violation)
    /// before this fix. This mock-level test can only pin "the override is invoked with the
    /// right tenantId" — the actual RLS behavior is verified live against real Postgres by
    /// LoyaltyJoinRlsIntegrationTests, since a mocked ITenantSessionOverride structurally cannot
    /// prove anything about Postgres session variables.
    /// </summary>
    [Fact]
    public async Task JoinAsync_new_member_runs_customer_and_membership_creation_through_tenant_override()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "Ірина Петренко", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _totp.GenerateSecret().Returns("SECRET");

        await _sut.JoinAsync(consumerId, tenantId);

        await _tenantScope.Received(1).ExecuteAsync(
            tenantId, Arg.Any<Func<Task<LoyaltyMembership>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinAsync_reuses_existing_customer_by_phone()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existingCustomer = new Customer { TenantId = tenantId, Name = "Old Name", Phone = "+380501234567" };
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "New Name", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).Returns(existingCustomer);

        await _sut.JoinAsync(consumerId, tenantId);

        await _customers.DidNotReceive().CreateAsync(Arg.Any<Customer>(), default);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.CustomerId == existingCustomer.Id), default);
    }

    // ── GetConsumerCodeAsync ───────────────────────────────────────────────
    // NOTE: pre-existing test breakage fixed incidentally while implementing TASK-498 — an
    // uncommitted, unrelated WIP change (visible via `git diff HEAD`) renamed
    // LoyaltyService.GetCurrentCodeAsync(consumerId, tenantId) to the current
    // GetConsumerCodeAsync(consumerId) — a cross-tenant consumer code keyed off
    // ConsumerAccount.LoyaltyTotpSecret instead of a per-membership TotpSecret — without
    // updating these two tests, which left ShelfGuard.Tests failing to compile at all. Not part
    // of TASK-498's scope; fixed only so `dotnet build`/`dotnet test` could run to verify this
    // task's own changes.

    [Fact]
    public async Task GetConsumerCodeAsync_unknown_consumer_returns_404()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).ReturnsNull();

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(code);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetConsumerCodeAsync_active_consumer_returns_code_payload()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _totp.GenerateCode("SECRET").Returns("654321");

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(error);
        Assert.NotNull(code);
        Assert.Equal($"SGCUS1.{consumerId}.654321", code.Code);
        Assert.Equal(30, code.ExpiresInSeconds);
    }

    // ── ResolveCodeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ResolveCodeAsync_malformed_payload_returns_400()
    {
        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            Guid.NewGuid(), Guid.NewGuid(), "not-a-valid-code");

        Assert.Null(result);
        Assert.Equal(400, statusCode);
    }

    [Fact]
    public async Task ResolveCodeAsync_locked_out_membership_returns_429_without_querying_repo()
    {
        var membershipId = Guid.NewGuid();
        _attempts.IsLockedOut(membershipId).Returns(true);

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            Guid.NewGuid(), Guid.NewGuid(), $"SGLOY1.{membershipId}.123456");

        Assert.Null(result);
        Assert.Equal(429, statusCode);
        await _loyalty.DidNotReceive().GetMembershipByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCodeAsync_unknown_membership_registers_failure_and_returns_400()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).ReturnsNull();

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            tenantId, Guid.NewGuid(), $"SGLOY1.{membershipId}.123456");

        Assert.Null(result);
        Assert.Equal(400, statusCode);
        _attempts.Received(1).RegisterFailure(membershipId, 5, Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task ResolveCodeAsync_blocked_membership_returns_400_without_registering_failure()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(
            new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Status = LoyaltyMembershipStatus.Blocked, TotpSecret = "SECRET" });

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            tenantId, Guid.NewGuid(), $"SGLOY1.{membershipId}.123456");

        Assert.Null(result);
        Assert.Equal(400, statusCode);
        _attempts.DidNotReceive().RegisterFailure(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task ResolveCodeAsync_wrong_code_registers_failure_and_returns_400()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(
            new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Status = LoyaltyMembershipStatus.Active, TotpSecret = "SECRET" });
        _totp.VerifyCode("SECRET", "999999").Returns((long?)null);

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            tenantId, Guid.NewGuid(), $"SGLOY1.{membershipId}.999999");

        Assert.Null(result);
        Assert.Equal(400, statusCode);
        _attempts.Received(1).RegisterFailure(membershipId, 5, Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task ResolveCodeAsync_replayed_timestep_returns_409()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(
            new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Status = LoyaltyMembershipStatus.Active, TotpSecret = "SECRET" });
        _totp.VerifyCode("SECRET", "123456").Returns(42L);
        _loyalty.TryClaimTimestepAsync(membershipId, tenantId, 42L, default).Returns(false);

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            tenantId, Guid.NewGuid(), $"SGLOY1.{membershipId}.123456");

        Assert.Null(result);
        Assert.Equal(409, statusCode);
    }

    [Fact]
    public async Task ResolveCodeAsync_success_returns_masked_phone_and_resets_attempts()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var consumerAccountId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(new LoyaltyMembership
        {
            Id = membershipId,
            TenantId = tenantId,
            Status = LoyaltyMembershipStatus.Active,
            TotpSecret = "SECRET",
            CustomerId = customerId,
            ConsumerAccountId = consumerAccountId,
            Balance = 15m,
        });
        _totp.VerifyCode("SECRET", "123456").Returns(42L);
        _loyalty.TryClaimTimestepAsync(membershipId, tenantId, 42L, default).Returns(true);
        _customers.GetByIdAsync(customerId, tenantId, default)
            .Returns(new Customer { Id = customerId, TenantId = tenantId, Name = "Ірина" });
        _consumerAccounts.GetByIdAsync(consumerAccountId, default)
            .Returns(new ConsumerAccount { Id = consumerAccountId, Phone = "+380501234567", FullName = "Ірина" });

        var (result, error, statusCode) = await _sut.ResolveCodeAsync(
            tenantId, Guid.NewGuid(), $"SGLOY1.{membershipId}.123456");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("Ірина", result.CustomerName);
        Assert.Equal(15m, result.Balance);
        Assert.EndsWith("4567", result.MaskedPhone);
        Assert.DoesNotContain("501", result.MaskedPhone);
        _attempts.Received(1).Reset(membershipId);
    }

    // ── ManualAdjustAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ManualAdjustAsync_membership_not_found_returns_404()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).ReturnsNull();

        var (membership, error, statusCode) = await _sut.ManualAdjustAsync(
            tenantId, Guid.NewGuid(), new ManualLoyaltyAdjustRequest(membershipId, 10m, "test"));

        Assert.Null(membership);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task ManualAdjustAsync_negative_result_returns_400_and_does_not_mutate()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var existing = new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Balance = 5m };
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(existing);

        var (membership, error, statusCode) = await _sut.ManualAdjustAsync(
            tenantId, Guid.NewGuid(), new ManualLoyaltyAdjustRequest(membershipId, -10m, "oops"));

        Assert.Null(membership);
        Assert.Equal(400, statusCode);
        Assert.Equal(5m, existing.Balance);
        await _loyalty.DidNotReceive().AddLedgerEntryAsync(Arg.Any<LoyaltyLedgerEntry>(), default);
    }

    [Fact]
    public async Task ManualAdjustAsync_valid_adjustment_updates_balance_and_logs()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var existing = new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Balance = 5m };
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(existing);
        _tenants.GetByIdAsync(tenantId, default).Returns(Tenant.Create("Acme", "acme"));

        var (membership, error, statusCode) = await _sut.ManualAdjustAsync(
            tenantId, Guid.NewGuid(), new ManualLoyaltyAdjustRequest(membershipId, 20m, "bonus"));

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(25m, membership.Balance);
        await _loyalty.Received(1).AddLedgerEntryAsync(
            Arg.Is<LoyaltyLedgerEntry>(e =>
                e.Amount == 20m && e.BalanceAfter == 25m && e.EntryType == LoyaltyEntryType.ManualAdjustment),
            default);
        await _activityLogs.Received(1).LogAsync(Arg.Any<ActivityLog>(), default);
    }

    /// <summary>
    /// TASK-414 (security review TASK-412, finding B): LoyaltyMembership now carries an xmin
    /// concurrency token (AppDbContext) so a race between this call and another writer to the
    /// same membership's Balance (a concurrent POS redemption, or a second ManualAdjustAsync
    /// call) surfaces as ConcurrencyConflictException from SaveChangesAsync instead of silently
    /// losing one of the two updates. This test pins the service-layer half of that fix in
    /// isolation (same shape as PosServiceTests'
    /// CreateSale_concurrency_conflict_on_commit_returns_409): ManualAdjustAsync must translate
    /// the exception into a clean 409 rather than letting it propagate unhandled, and must not
    /// log an activity entry for a change that was never actually persisted.
    /// </summary>
    [Fact]
    public async Task ManualAdjustAsync_concurrency_conflict_returns_409()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var existing = new LoyaltyMembership { Id = membershipId, TenantId = tenantId, Balance = 5m };
        _loyalty.GetMembershipByIdAsync(membershipId, tenantId, default).Returns(existing);
        _loyalty.SaveChangesAsync(default)
            .Throws(new ConcurrencyConflictException("simulated concurrent write conflict"));

        var (membership, error, statusCode) = await _sut.ManualAdjustAsync(
            tenantId, Guid.NewGuid(), new ManualLoyaltyAdjustRequest(membershipId, 20m, "bonus"));

        Assert.Null(membership);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
        await _activityLogs.DidNotReceive().LogAsync(Arg.Any<ActivityLog>(), default);
    }

    // ── JoinAsStaffAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task JoinAsStaffAsync_no_phone_on_profile_returns_400()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _users.GetByIdAsync(userId, default).Returns(MakeUserWithPhone(null));

        var (membership, error, statusCode) = await _sut.JoinAsStaffAsync(tenantId, userId);

        Assert.Null(membership);
        Assert.Equal(400, statusCode);
    }

    [Fact]
    public async Task JoinAsStaffAsync_idempotent_backfills_linked_user_on_existing_membership()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var consumerId = Guid.NewGuid();
        _users.GetByIdAsync(userId, default).Returns(MakeUserWithPhone("+380501234567"));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default)
            .Returns(new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X" });
        var existingMembership = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, LinkedUserId = null };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existingMembership);

        var (membership, error, statusCode) = await _sut.JoinAsStaffAsync(tenantId, userId);

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(userId, existingMembership.LinkedUserId);
        await _loyalty.DidNotReceive().AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    [Fact]
    public async Task JoinAsStaffAsync_new_creates_consumer_account_and_membership()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _users.GetByIdAsync(userId, default).Returns(MakeUserWithPhone("+380501234567"));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default).ReturnsNull();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, Arg.Any<Guid>(), default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _hasher.Hash(Arg.Any<string>()).Returns("placeholder_hash");
        _totp.GenerateSecret().Returns("SECRET");

        var (membership, error, statusCode) = await _sut.JoinAsStaffAsync(tenantId, userId);

        Assert.Null(error);
        Assert.NotNull(membership);
        await _consumerAccounts.Received(1).AddAsync(
            Arg.Is<ConsumerAccount>(a => a.Phone == "+380501234567"), default);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.LinkedUserId == userId && m.TenantId == tenantId), default);
    }

    // ── ResolveOrCreateMembershipByPhoneAsync (TASK-498) ──────────────────

    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_new_consumer_creates_membership()
    {
        var tenantId = Guid.NewGuid();
        var consumerId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default)
            .Returns(new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "Ірина Петренко", IsActive = true });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _totp.GenerateSecret().Returns("SECRET");

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.IsNewMembership);
        Assert.Equal(0m, result.Balance);
        Assert.Equal("Ірина Петренко", result.ConsumerFullName);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.TenantId == tenantId && m.ConsumerAccountId == consumerId), default);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_existing_membership_returns_idempotently_and_keeps_balance()
    {
        var tenantId = Guid.NewGuid();
        var consumerId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default)
            .Returns(new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "Ірина", IsActive = true });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default)
            .Returns(new LoyaltyMembership { Id = membershipId, TenantId = tenantId, ConsumerAccountId = consumerId, Balance = 77m });

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result.IsNewMembership);
        Assert.Equal(77m, result.Balance);
        Assert.Equal(membershipId, result.MembershipId);
        await _loyalty.DidNotReceive().AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    /// <summary>Proves multi-tenant membership independence: a membership at a different tenant
    /// must not block (or be reused for) a brand-new membership at this tenant.</summary>
    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_membership_at_another_tenant_only_still_creates_new_one_here()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var consumerId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default)
            .Returns(new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "Ірина", IsActive = true });
        _loyalty.GetMembershipByTenantConsumerAsync(otherTenantId, consumerId, default)
            .Returns(new LoyaltyMembership { TenantId = otherTenantId, ConsumerAccountId = consumerId, Balance = 500m });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _totp.GenerateSecret().Returns("SECRET");

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.IsNewMembership);
        Assert.Equal(0m, result.Balance); // independent of the other tenant's 500m balance
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.TenantId == tenantId && m.ConsumerAccountId == consumerId), default);
    }

    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_no_matching_consumer_returns_null_result_without_error()
    {
        var tenantId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default).ReturnsNull();

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(result);
        Assert.Null(error);
        await _loyalty.DidNotReceive().AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_module_disabled_returns_null_result_without_error_or_lookup()
    {
        var tenantId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant()); // no modules

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(result);
        Assert.Null(error);
        await _consumerAccounts.DidNotReceive().GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _loyalty.DidNotReceive().AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("not-a-phone")]
    public async Task ResolveOrCreateMembershipByPhoneAsync_invalid_phone_returns_400_error(string rawPhone)
    {
        var tenantId = Guid.NewGuid();

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, rawPhone);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal(400, statusCode);
        await _tenants.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── GetMyMembershipAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetMyMembershipAsync_no_membership_returns_null()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _loyalty.GetMembershipByLinkedUserAsync(tenantId, userId, default).ReturnsNull();

        var result = await _sut.GetMyMembershipAsync(tenantId, userId);

        Assert.Null(result);
    }

    // ── Settings ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSettingsAsync_no_saved_row_returns_defaults_with_null_updatedAt()
    {
        var tenantId = Guid.NewGuid();
        _loyalty.GetSettingsAsync(tenantId, default).ReturnsNull();

        var dto = await _sut.GetSettingsAsync(tenantId);

        Assert.True(dto.IsEnabled);
        Assert.Equal(3.0m, dto.AccrualRatePercent);
        Assert.Equal(50.0m, dto.RedemptionCapPercent);
        Assert.Null(dto.UpdatedAt);
    }

    [Theory]
    [InlineData(150, 50, 0, 30)]   // AccrualRatePercent out of range
    [InlineData(3, 150, 0, 30)]    // RedemptionCapPercent out of range
    [InlineData(3, 50, -1, 30)]    // MinRedemptionBalance negative
    [InlineData(3, 50, 0, 1)]      // CodeTtlSeconds too low
    public async Task UpsertSettingsAsync_invalid_values_returns_error(
        decimal accrual, decimal cap, decimal minBalance, int ttl)
    {
        var (dto, error) = await _sut.UpsertSettingsAsync(
            Guid.NewGuid(), new UpsertLoyaltyProgramSettingsRequest(true, accrual, cap, minBalance, ttl));

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpsertSettingsAsync_valid_request_creates_new_row_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        _loyalty.GetSettingsAsync(tenantId, default).ReturnsNull();

        var (dto, error) = await _sut.UpsertSettingsAsync(
            tenantId, new UpsertLoyaltyProgramSettingsRequest(true, 5m, 40m, 10m, 25));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(5m, dto.AccrualRatePercent);
        await _loyalty.Received(1).AddSettingsAsync(Arg.Any<LoyaltyProgramSettings>(), default);
        _loyalty.DidNotReceive().UpdateSettings(Arg.Any<LoyaltyProgramSettings>());
    }

    [Fact]
    public async Task UpsertSettingsAsync_valid_request_updates_existing_row()
    {
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyProgramSettings { TenantId = tenantId };
        _loyalty.GetSettingsAsync(tenantId, default).Returns(existing);

        var (dto, error) = await _sut.UpsertSettingsAsync(
            tenantId, new UpsertLoyaltyProgramSettingsRequest(false, 7m, 60m, 5m, 45));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.False(dto.IsEnabled);
        Assert.Equal(7m, dto.AccrualRatePercent);
        await _loyalty.DidNotReceive().AddSettingsAsync(Arg.Any<LoyaltyProgramSettings>(), default);
        _loyalty.Received(1).UpdateSettings(existing);
    }
}
