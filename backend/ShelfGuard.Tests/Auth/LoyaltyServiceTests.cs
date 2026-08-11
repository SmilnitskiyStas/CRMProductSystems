using System.Linq;
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
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IResolveCodeAttemptTracker _attempts = Substitute.For<IResolveCodeAttemptTracker>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITenantSessionOverride _tenantScope = Substitute.For<ITenantSessionOverride>();
    private readonly LoyaltyService _sut;

    public LoyaltyServiceTests()
    {
        _sut = new LoyaltyService(
            _loyalty, _customers, _tenants, _users, _consumerAccounts, _locations, _hasher, _totp,
            _attempts, _activityLogs, _tenantScope, NullLogger<LoyaltyService>.Instance);

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

        // TASK-499: GetConsumerCodeAsync's format-resolution helper also runs through
        // ITenantSessionOverride (loyalty_program_settings has no consumer_self_access RLS
        // policy) — same pure pass-through as the LoyaltyMembership setup above, just for the
        // LoyaltyProgramSettings? closed generic instead.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<LoyaltyProgramSettings?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<LoyaltyProgramSettings?>>>()());

        // TASK-501/507: GetAvailableNetworksAsync's combined settings+stores read is its own
        // closed generic (a value tuple) — same pure pass-through pattern as the two setups
        // above, just for that tuple's Task<T> instead.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(),
                Arg.Any<Func<Task<(LoyaltyProgramSettings? Settings, IReadOnlyList<LoyaltyNetworkStoreDto> Stores)>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => ci
                .Arg<Func<Task<(LoyaltyProgramSettings? Settings, IReadOnlyList<LoyaltyNetworkStoreDto> Stores)>>>()());

        // TASK-507: GetMembershipsForConsumerAsync's per-membership preferred-store resolution
        // and SetPreferredStoreAsync both run through this same override — pure pass-through
        // for the Location? closed generic.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<Location?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<Location?>>>()());

        // TASK-507: SetPreferredStoreAsync's combined membership-check+store-validate+write
        // read is its own closed generic (a 4-tuple) — same pure pass-through pattern.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(),
                Arg.Any<Func<Task<(LoyaltyMembership? Membership, Location? Location, string? Error, int? StatusCode)>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => ci
                .Arg<Func<Task<(LoyaltyMembership? Membership, Location? Location, string? Error, int? StatusCode)>>>()());

        // Default: no locations for any tenant unless a test overrides it — keeps
        // GetAvailableNetworksAsync's Stores empty-but-non-null by default.
        _locations.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Location>());
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
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns(new List<LoyaltyMembership>());
        _totp.GenerateCode("SECRET").Returns("654321");

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(error);
        Assert.NotNull(code);
        Assert.Equal($"SGCUS1.{consumerId}.654321", code.Code);
        Assert.Equal(30, code.ExpiresInSeconds);
    }

    // ── GetConsumerCodeAsync — TASK-499 DisplayFormat resolution ────────────

    [Fact]
    public async Task GetConsumerCodeAsync_no_tenantId_zero_memberships_returns_barcode_default()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns(new List<LoyaltyMembership>());

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(error);
        Assert.NotNull(code);
        Assert.Equal("barcode", code.DisplayFormat);
    }

    [Fact]
    public async Task GetConsumerCodeAsync_no_tenantId_one_membership_returns_that_tenants_saved_format()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns(
            new List<LoyaltyMembership> { new() { TenantId = tenantId, ConsumerAccountId = consumerId } });
        _loyalty.GetSettingsAsync(tenantId, default).Returns(
            new LoyaltyProgramSettings { TenantId = tenantId, CustomerCodeFormat = "qr" });

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(error);
        Assert.Equal("qr", code!.DisplayFormat);
    }

    [Fact]
    public async Task GetConsumerCodeAsync_no_tenantId_one_membership_no_saved_settings_returns_barcode()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns(
            new List<LoyaltyMembership> { new() { TenantId = tenantId, ConsumerAccountId = consumerId } });
        _loyalty.GetSettingsAsync(tenantId, default).ReturnsNull();

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(error);
        Assert.Equal("barcode", code!.DisplayFormat);
    }

    [Fact]
    public async Task GetConsumerCodeAsync_no_tenantId_multiple_memberships_returns_409_ambiguous()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns(new List<LoyaltyMembership>
        {
            new() { TenantId = Guid.NewGuid(), ConsumerAccountId = consumerId },
            new() { TenantId = Guid.NewGuid(), ConsumerAccountId = consumerId },
        });

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId);

        Assert.Null(code);
        Assert.Equal("network_selection_required", error);
        Assert.Equal(409, statusCode);
    }

    [Fact]
    public async Task GetConsumerCodeAsync_explicit_tenantId_not_a_member_returns_403()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId, tenantId);

        Assert.Null(code);
        Assert.Equal(403, statusCode);
        await _loyalty.DidNotReceive().GetMembershipsForConsumerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetConsumerCodeAsync_explicit_tenantId_member_returns_that_tenants_format_bypassing_ambiguity_check()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(
            new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "X", IsActive = true, LoyaltyTotpSecret = "SECRET" });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default)
            .Returns(new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId });
        _loyalty.GetSettingsAsync(tenantId, default).Returns(
            new LoyaltyProgramSettings { TenantId = tenantId, CustomerCodeFormat = "qr" });

        // Consumer also has other memberships elsewhere — must NOT trigger the 2+ ambiguity
        // check, since an explicit tenantId always bypasses it.
        var (code, error, statusCode) = await _sut.GetConsumerCodeAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.Equal("qr", code!.DisplayFormat);
        await _loyalty.DidNotReceive().GetMembershipsForConsumerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── GetMembershipsForConsumerAsync — TASK-507 preferred store resolution ─

    [Fact]
    public async Task GetMembershipsForConsumerAsync_resolves_preferred_store_name_and_address()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = storeId,
        };
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns([membership]);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = tenantId, Name = "М3", Address = "вул. Шевченка, 10", IsActive = true });

        var result = await _sut.GetMembershipsForConsumerAsync(consumerId);

        var dto = Assert.Single(result);
        Assert.Equal(storeId, dto.PreferredStoreId);
        Assert.Equal("М3", dto.PreferredStoreName);
        Assert.Equal("вул. Шевченка, 10", dto.PreferredStoreAddress);
    }

    [Fact]
    public async Task GetMembershipsForConsumerAsync_no_preferred_store_returns_null_names_without_lookup()
    {
        var consumerId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = Guid.NewGuid(), ConsumerAccountId = consumerId, PreferredStoreId = null,
        };
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns([membership]);

        var result = await _sut.GetMembershipsForConsumerAsync(consumerId);

        var dto = Assert.Single(result);
        Assert.Null(dto.PreferredStoreId);
        Assert.Null(dto.PreferredStoreName);
        Assert.Null(dto.PreferredStoreAddress);
        await _locations.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMembershipsForConsumerAsync_stale_preferred_store_returns_null_names_without_throwing()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = storeId,
        };
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns([membership]);
        // Store since removed — GetByIdAsync returns null (default NSubstitute behavior).

        var result = await _sut.GetMembershipsForConsumerAsync(consumerId);

        var dto = Assert.Single(result);
        Assert.Equal(storeId, dto.PreferredStoreId); // raw reference is preserved
        Assert.Null(dto.PreferredStoreName);
        Assert.Null(dto.PreferredStoreAddress);
    }

    [Fact]
    public async Task GetMembershipsForConsumerAsync_inactive_preferred_store_returns_null_names()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = storeId,
        };
        _loyalty.GetMembershipsForConsumerAsync(consumerId, default).Returns([membership]);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = tenantId, Name = "Closed Down", IsActive = false });

        var result = await _sut.GetMembershipsForConsumerAsync(consumerId);

        var dto = Assert.Single(result);
        Assert.Null(dto.PreferredStoreName);
        Assert.Null(dto.PreferredStoreAddress);
    }

    // ── SetPreferredStoreAsync (TASK-507) ─────────────────────────────────

    [Fact]
    public async Task SetPreferredStoreAsync_no_membership_at_tenant_returns_403_without_mutation()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, storeId);

        Assert.Null(membership);
        Assert.Equal("You are not a member of this network.", error);
        Assert.Equal(403, statusCode);
        await _locations.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
    }

    [Fact]
    public async Task SetPreferredStoreAsync_store_belongs_to_different_tenant_returns_400_without_mutation()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var existing = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = null };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = otherTenantId, Name = "Someone Else's Store", IsActive = true });

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, storeId);

        Assert.Null(membership);
        Assert.Equal("Invalid store for this network.", error);
        Assert.Equal(400, statusCode);
        Assert.Null(existing.PreferredStoreId);
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
    }

    [Fact]
    public async Task SetPreferredStoreAsync_inactive_store_returns_400_without_mutation()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var existing = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = null };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = tenantId, Name = "Closed Down", IsActive = false });

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, storeId);

        Assert.Null(membership);
        Assert.Equal("Invalid store for this network.", error);
        Assert.Equal(400, statusCode);
        Assert.Null(existing.PreferredStoreId);
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
    }

    [Fact]
    public async Task SetPreferredStoreAsync_non_shoppable_store_type_returns_400_without_mutation()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var existing = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = null };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = tenantId, Name = "Main Warehouse", Type = "warehouse", IsActive = true });

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, storeId);

        Assert.Null(membership);
        Assert.Equal("Invalid store for this network.", error);
        Assert.Equal(400, statusCode);
        Assert.Null(existing.PreferredStoreId);
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
    }

    [Fact]
    public async Task SetPreferredStoreAsync_valid_store_persists_and_returns_200_with_resolved_fields()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var existing = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = null };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _locations.GetByIdAsync(storeId, default).Returns(
            new Location { Id = storeId, TenantId = tenantId, Name = "М3", Address = "вул. Шевченка, 10", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, storeId);

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(storeId, membership.PreferredStoreId);
        Assert.Equal("М3", membership.PreferredStoreName);
        Assert.Equal("вул. Шевченка, 10", membership.PreferredStoreAddress);
        Assert.Equal(storeId, existing.PreferredStoreId);
        _loyalty.Received(1).UpdateMembership(existing);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task SetPreferredStoreAsync_setting_again_to_a_different_store_overwrites_not_additive()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var firstStoreId = Guid.NewGuid();
        var secondStoreId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, PreferredStoreId = firstStoreId,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _locations.GetByIdAsync(secondStoreId, default).Returns(
            new Location { Id = secondStoreId, TenantId = tenantId, Name = "Другий магазин", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));

        var (membership, error, statusCode) = await _sut.SetPreferredStoreAsync(consumerId, tenantId, secondStoreId);

        Assert.Null(error);
        Assert.Equal(secondStoreId, membership!.PreferredStoreId);
        Assert.Equal(secondStoreId, existing.PreferredStoreId);
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
    public async Task GetAvailableNetworksAsync_returns_only_active_enabled_loyalty_networks()
    {
        var available = MakeTenant("loyalty");
        var disabled = Tenant.Create("Disabled", "disabled");
        disabled.UpdateModules(["loyalty"]);
        var noModule = Tenant.Create("No loyalty", "no-loyalty");
        var inactive = Tenant.Create("Inactive", "inactive");
        inactive.UpdateModules(["loyalty"]);
        inactive.Deactivate();
        _tenants.GetAllAsync(default).Returns([available, disabled, noModule, inactive]);
        _loyalty.GetSettingsAsync(available.Id, default).ReturnsNull();
        _loyalty.GetSettingsAsync(disabled.Id, default)
            .Returns(new LoyaltyProgramSettings { TenantId = disabled.Id, IsEnabled = false });

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(available.Id, network.TenantId);
        Assert.Equal(available.Name, network.TenantName);
        // No locations stubbed for this tenant (constructor default: empty list) — a
        // zero-store tenant must still appear, with an empty (not null) Stores.
        Assert.Empty(network.Stores);
    }

    // ── GetAvailableNetworksAsync — TASK-501/507 Stores ─────────────────────

    private static Location MakeLocation(
        Guid tenantId, string name, bool isActive = true, string type = "retail_store", string? address = null) =>
        new() { TenantId = tenantId, Name = name, IsActive = isActive, Type = type, Address = address };

    [Fact]
    public async Task GetAvailableNetworksAsync_includes_active_shoppable_stores_sorted_alphabetically_with_id_and_address()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([tenant]);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        var store1 = MakeLocation(tenant.Id, "Магазин №1 - Центральний", address: "вул. Хрещатик, 1");
        var store2 = MakeLocation(tenant.Id, "М3", address: "вул. Шевченка, 10");
        _locations.GetAllAsync(default).Returns(new List<Location> { store1, store2 });

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(
            new[]
            {
                new LoyaltyNetworkStoreDto(store2.Id, "М3", "вул. Шевченка, 10"),
                new LoyaltyNetworkStoreDto(store1.Id, "Магазин №1 - Центральний", "вул. Хрещатик, 1"),
            },
            network.Stores);
    }

    [Fact]
    public async Task GetAvailableNetworksAsync_excludes_inactive_stores()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([tenant]);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        _locations.GetAllAsync(default).Returns(new List<Location>
        {
            MakeLocation(tenant.Id, "Active Store"),
            MakeLocation(tenant.Id, "Closed Down", isActive: false),
        });

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(new[] { "Active Store" }, network.Stores.Select(s => s.StoreName));
    }

    [Fact]
    public async Task GetAvailableNetworksAsync_excludes_non_shoppable_location_types()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([tenant]);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        _locations.GetAllAsync(default).Returns(new List<Location>
        {
            MakeLocation(tenant.Id, "Front Store"),
            MakeLocation(tenant.Id, "Main Warehouse", type: "warehouse"),
            MakeLocation(tenant.Id, "HQ Office", type: "office"),
        });

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(new[] { "Front Store" }, network.Stores.Select(s => s.StoreName));
    }

    [Fact]
    public async Task GetAvailableNetworksAsync_zero_stores_still_includes_tenant_with_empty_stores()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([tenant]);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        _locations.GetAllAsync(default).Returns(new List<Location>());

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(tenant.Id, network.TenantId);
        Assert.NotNull(network.Stores);
        Assert.Empty(network.Stores);
    }

    [Fact]
    public async Task GetSettingsAsync_no_saved_row_returns_defaults_with_null_updatedAt()
    {
        var tenantId = Guid.NewGuid();
        _loyalty.GetSettingsAsync(tenantId, default).ReturnsNull();

        var dto = await _sut.GetSettingsAsync(tenantId);

        Assert.True(dto.IsEnabled);
        Assert.Equal(3.0m, dto.AccrualRatePercent);
        Assert.Equal(50.0m, dto.RedemptionCapPercent);
        Assert.Equal("barcode", dto.CustomerCodeFormat);
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
            Guid.NewGuid(), new UpsertLoyaltyProgramSettingsRequest(true, accrual, cap, minBalance, ttl, "barcode"));

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("QR")]           // case-sensitive — must not silently accept
    [InlineData("qrcode")]
    public async Task UpsertSettingsAsync_unrecognized_customerCodeFormat_returns_error(string? format)
    {
        var (dto, error) = await _sut.UpsertSettingsAsync(
            Guid.NewGuid(), new UpsertLoyaltyProgramSettingsRequest(true, 3m, 50m, 0m, 30, format!));

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpsertSettingsAsync_valid_request_creates_new_row_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        _loyalty.GetSettingsAsync(tenantId, default).ReturnsNull();

        var (dto, error) = await _sut.UpsertSettingsAsync(
            tenantId, new UpsertLoyaltyProgramSettingsRequest(true, 5m, 40m, 10m, 25, "qr"));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(5m, dto.AccrualRatePercent);
        Assert.Equal("qr", dto.CustomerCodeFormat);
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
            tenantId, new UpsertLoyaltyProgramSettingsRequest(false, 7m, 60m, 5m, 45, "barcode"));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.False(dto.IsEnabled);
        Assert.Equal(7m, dto.AccrualRatePercent);
        Assert.Equal("barcode", dto.CustomerCodeFormat);
        await _loyalty.DidNotReceive().AddSettingsAsync(Arg.Any<LoyaltyProgramSettings>(), default);
        _loyalty.Received(1).UpdateSettings(existing);
    }

    [Theory]
    [InlineData("qr")]
    [InlineData("barcode")]
    public async Task UpsertSettingsAsync_round_trips_customerCodeFormat(string format)
    {
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyProgramSettings { TenantId = tenantId };
        _loyalty.GetSettingsAsync(tenantId, default).Returns(existing);

        var (dto, error) = await _sut.UpsertSettingsAsync(
            tenantId, new UpsertLoyaltyProgramSettingsRequest(true, 3m, 50m, 0m, 30, format));

        Assert.Null(error);
        Assert.Equal(format, dto!.CustomerCodeFormat);
        Assert.Equal(format, existing.CustomerCodeFormat);
    }
}
