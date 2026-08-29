using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Features.MobileConfig;
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
    private readonly IConsumerFeatureFlagService _featureFlags = Substitute.For<IConsumerFeatureFlagService>();
    private readonly LoyaltyService _sut;

    public LoyaltyServiceTests()
    {
        _sut = new LoyaltyService(
            _loyalty, _customers, _tenants, _users, _consumerAccounts, _locations, _hasher, _totp,
            _attempts, _activityLogs, _tenantScope, _featureFlags, NullLogger<LoyaltyService>.Instance);

        // TASK-559: production-safety default — every tenant's "loyalty" consumer-app flag
        // resolves enabled unless a specific test overrides it, matching
        // IConsumerFeatureFlagService's own documented default-enabled contract. Keeps every
        // pre-existing GetAvailableNetworksAsync test in this file passing unchanged.
        _featureFlags.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

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

        // TASK-615: GetTierProgressAsync reads loyalty_tier_definitions (staff-only, no
        // consumer_self_access RLS policy) through this same override — pure pass-through for
        // the List<LoyaltyTierDefinition> closed generic, same pattern as the setups above.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<List<LoyaltyTierDefinition>>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<List<LoyaltyTierDefinition>>>>()());

        // TASK-627: CreateMembershipCoreAsync/JoinAsStaffAsync now read the tier ladder directly
        // (not through ITenantSessionOverride) to assign an entry tier on membership creation.
        // Default every tenant to "no ladder configured" so every pre-existing JoinAsync/
        // ResolveOrCreateMembershipByPhoneAsync/JoinAsStaffAsync test in this file keeps its
        // pre-TASK-627 behavior (null CurrentTierId, no history row) unless a test explicitly
        // configures a ladder for its own tenantId — same "safe default" pattern as
        // _locations.GetAllAsync above.
        _loyalty.GetTierLadderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<LoyaltyTierDefinition>());
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

    // ── JoinAsync — entry-tier assignment on creation (TASK-627) ───────────

    [Fact]
    public async Task JoinAsync_new_member_with_configured_ladder_assigns_entry_tier()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0 };
        var gold = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Gold", SortOrder = 1 };
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze, gold });

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.CurrentTierId == bronze.Id && m.CompositeScore == 0m
                && m.TierScoreUpdatedAt != null),
            default);
        await _loyalty.Received(1).AddTierHistoryAsync(
            Arg.Is<LoyaltyTierChangeHistory>(h =>
                h.TenantId == tenantId && h.FromTierId == null && h.ToTierId == bronze.Id
                && h.FromScore == 0m && h.ToScore == 0m),
            default);
    }

    [Fact]
    public async Task JoinAsync_new_member_with_no_ladder_leaves_tier_unassigned()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition>());

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.CurrentTierId == null && m.CompositeScore == 0m
                && m.TierScoreUpdatedAt == null),
            default);
        await _loyalty.DidNotReceive().AddTierHistoryAsync(Arg.Any<LoyaltyTierChangeHistory>(), default);
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

    /// <summary>TASK-627: ResolveOrCreateMembershipByPhoneAsync's auto-enroll path goes through
    /// CreateMembershipCoreAsync just like JoinAsync, so it gets the same entry-tier treatment.</summary>
    [Fact]
    public async Task ResolveOrCreateMembershipByPhoneAsync_new_consumer_with_configured_ladder_assigns_entry_tier()
    {
        var tenantId = Guid.NewGuid();
        var consumerId = Guid.NewGuid();
        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0 };
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _consumerAccounts.GetByPhoneAsync("+380501234567", default)
            .Returns(new ConsumerAccount { Id = consumerId, Phone = "+380501234567", FullName = "Ірина", IsActive = true });
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenantId, default).ReturnsNull();
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze });

        var (result, error, statusCode) = await _sut.ResolveOrCreateMembershipByPhoneAsync(tenantId, "0501234567");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.IsNewMembership);
        await _loyalty.Received(1).AddMembershipAsync(
            Arg.Is<LoyaltyMembership>(m => m.CurrentTierId == bronze.Id && m.CompositeScore == 0m), default);
        await _loyalty.Received(1).AddTierHistoryAsync(
            Arg.Is<LoyaltyTierChangeHistory>(h => h.ToTierId == bronze.Id && h.FromTierId == null), default);
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
        Assert.Equal(available.Slug, network.Slug); // TASK-548
        // No locations stubbed for this tenant (constructor default: empty list) — a
        // zero-store tenant must still appear, with an empty (not null) Stores.
        Assert.Empty(network.Stores);
    }

    // ── GetAvailableNetworksAsync — TASK-559 consumer-app feature-flag filter ──

    [Fact]
    public async Task GetAvailableNetworksAsync_excludes_tenant_with_loyalty_feature_flag_disabled()
    {
        var enabledTenant = MakeTenant("loyalty");
        var disabledTenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([enabledTenant, disabledTenant]);
        _loyalty.GetSettingsAsync(enabledTenant.Id, default).ReturnsNull();
        _featureFlags.IsEnabledAsync(disabledTenant.Id, "loyalty", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetAvailableNetworksAsync();

        var network = Assert.Single(result);
        Assert.Equal(enabledTenant.Id, network.TenantId);
    }

    /// <summary>
    /// TASK-559 DoD: "a tenant with zero MobileConfiguration activity ... still appears in
    /// GetNetworks" — at the mock level this is exactly the default-enabled stub set up in the
    /// constructor (mirroring IConsumerFeatureFlagService's real default-enabled contract); the
    /// real-Postgres proof against the actual flag service lives in
    /// LoyaltyFeatureGateRlsIntegrationTests.PRODUCTION_SAFETY_GetAvailableNetworksAsync_includes_tenant_with_zero_MobileConfiguration_activity.
    /// </summary>
    [Fact]
    public async Task GetAvailableNetworksAsync_includes_tenant_when_flag_service_defaults_enabled()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([tenant]);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        // No explicit _featureFlags stub for this tenant — falls through to the constructor's
        // blanket "IsEnabledAsync returns true" default, same as a tenant with no published
        // MobileConfigurationVersion resolves for the real service.

        var result = await _sut.GetAvailableNetworksAsync();

        Assert.Single(result);
    }

    /// <summary>
    /// TASK-559 N+1 note: the feature-flag check is deliberately checked BEFORE the tenant-scoped
    /// settings/store load (<see cref="ITenantSessionOverride"/>) so a disabled tenant skips that
    /// second per-tenant round trip entirely, rather than paying for both. Pins that ordering.
    /// </summary>
    [Fact]
    public async Task GetAvailableNetworksAsync_disabled_flag_skips_the_tenant_scoped_settings_lookup()
    {
        var disabledTenant = MakeTenant("loyalty");
        _tenants.GetAllAsync(default).Returns([disabledTenant]);
        _featureFlags.IsEnabledAsync(disabledTenant.Id, "loyalty", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetAvailableNetworksAsync();

        Assert.Empty(result);
        await _loyalty.DidNotReceive().GetSettingsAsync(disabledTenant.Id, Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task UpsertSettingsAsync_round_trips_bonus_exclusions()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new LoyaltyProgramSettings { TenantId = tenantId };
        _loyalty.GetSettingsAsync(tenantId, default).Returns(existing);

        var request = new UpsertLoyaltyProgramSettingsRequest(
            true, 3m, 50m, 0m, 30, "barcode",
            BonusExclusionsEnabled: true,
            ExclusionsApplyToAccrual: true,
            ExclusionsApplyToRedemption: false,
            ExcludeDiscountedItems: true,
            ExcludedCategoryIds: [categoryId],
            ExcludedProductIds: [productId]);

        var (dto, error) = await _sut.UpsertSettingsAsync(tenantId, request);

        Assert.Null(error);
        Assert.True(dto!.BonusExclusionsEnabled);
        Assert.True(dto.ExclusionsApplyToAccrual);
        Assert.False(dto.ExclusionsApplyToRedemption);
        Assert.True(dto.ExcludeDiscountedItems);
        Assert.Equal([categoryId], dto.ExcludedCategoryIds);
        Assert.Equal([productId], dto.ExcludedProductIds);
    }

    // ── GetNetworkBySlugAsync (TASK-548) ──────────────────────────────────

    [Fact]
    public async Task GetNetworkBySlugAsync_unknown_slug_returns_404()
    {
        _tenants.GetBySlugAsync("ghost", default).ReturnsNull();

        var (network, error, statusCode) = await _sut.GetNetworkBySlugAsync("ghost");

        Assert.Null(network);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetNetworkBySlugAsync_inactive_tenant_returns_404()
    {
        var tenant = MakeTenant("loyalty");
        tenant.Deactivate();
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);

        var (network, error, statusCode) = await _sut.GetNetworkBySlugAsync("acme");

        Assert.Null(network);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetNetworkBySlugAsync_no_loyalty_module_returns_404()
    {
        var tenant = MakeTenant(); // no modules
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);

        var (network, error, statusCode) = await _sut.GetNetworkBySlugAsync("acme");

        Assert.Null(network);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetNetworkBySlugAsync_settings_disabled_returns_404()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _loyalty.GetSettingsAsync(tenant.Id, default)
            .Returns(new LoyaltyProgramSettings { TenantId = tenant.Id, IsEnabled = false });

        var (network, error, statusCode) = await _sut.GetNetworkBySlugAsync("acme");

        Assert.Null(network);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetNetworkBySlugAsync_eligible_tenant_returns_network_with_slug_and_stores()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();
        var store = MakeLocation(tenant.Id, "Флагман");
        _locations.GetAllAsync(default).Returns(new List<Location> { store });

        var (network, error, statusCode) = await _sut.GetNetworkBySlugAsync("acme");

        Assert.Null(error);
        Assert.NotNull(network);
        Assert.Equal(tenant.Id, network.TenantId);
        Assert.Equal(tenant.Name, network.TenantName);
        Assert.Equal(tenant.Slug, network.Slug);
        Assert.Single(network.Stores);
    }

    // ── GetPublicRetailerInfoAsync (TASK-549) ───────────────────────────────

    [Fact]
    public async Task GetPublicRetailerInfoAsync_unknown_slug_returns_404()
    {
        _tenants.GetBySlugAsync("ghost", default).ReturnsNull();

        var (info, error, statusCode) = await _sut.GetPublicRetailerInfoAsync("ghost");

        Assert.Null(info);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetPublicRetailerInfoAsync_inactive_tenant_returns_404()
    {
        var tenant = MakeTenant("loyalty");
        tenant.Deactivate();
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);

        var (info, error, statusCode) = await _sut.GetPublicRetailerInfoAsync("acme");

        Assert.Null(info);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetPublicRetailerInfoAsync_no_loyalty_module_returns_404()
    {
        var tenant = MakeTenant(); // no modules
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);

        var (info, error, statusCode) = await _sut.GetPublicRetailerInfoAsync("acme");

        Assert.Null(info);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetPublicRetailerInfoAsync_settings_disabled_returns_404()
    {
        var tenant = MakeTenant("loyalty");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _loyalty.GetSettingsAsync(tenant.Id, default)
            .Returns(new LoyaltyProgramSettings { TenantId = tenant.Id, IsEnabled = false });

        var (info, error, statusCode) = await _sut.GetPublicRetailerInfoAsync("acme");

        Assert.Null(info);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetPublicRetailerInfoAsync_eligible_tenant_returns_minimal_public_info()
    {
        var tenant = MakeTenant("loyalty");
        tenant.UpdateLogoUrl("https://cdn.example.com/acme-logo.png");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _loyalty.GetSettingsAsync(tenant.Id, default).ReturnsNull();

        var (info, error, statusCode) = await _sut.GetPublicRetailerInfoAsync("acme");

        Assert.Null(error);
        Assert.Null(statusCode);
        Assert.NotNull(info);
        Assert.Equal(tenant.Name, info.Name);
        Assert.Equal(tenant.Slug, info.Slug);
        Assert.Equal(tenant.LogoUrl, info.LogoUrl);
        Assert.True(info.Joinable);
    }

    // ── JoinBySlugAsync (TASK-548) ─────────────────────────────────────────

    [Fact]
    public async Task JoinBySlugAsync_unknown_slug_returns_404_without_touching_consumer()
    {
        _tenants.GetBySlugAsync("ghost", default).ReturnsNull();

        var (membership, error, statusCode) = await _sut.JoinBySlugAsync(Guid.NewGuid(), "ghost");

        Assert.Null(membership);
        Assert.Equal(404, statusCode);
        await _consumerAccounts.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinBySlugAsync_known_slug_delegates_to_JoinAsync_logic()
    {
        var consumerId = Guid.NewGuid();
        var tenant = MakeTenant("loyalty");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _tenants.GetByIdAsync(tenant.Id, default).Returns(tenant);
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _loyalty.GetMembershipByTenantConsumerAsync(tenant.Id, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync("+380501234567", tenant.Id, default).ReturnsNull();
        _totp.GenerateSecret().Returns("SECRET");

        var (membership, error, statusCode) = await _sut.JoinBySlugAsync(consumerId, "acme");

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(tenant.Id, membership.TenantId);
        await _loyalty.Received(1).AddMembershipAsync(Arg.Any<LoyaltyMembership>(), default);
    }

    [Fact]
    public async Task JoinBySlugAsync_module_not_active_returns_403()
    {
        var consumerId = Guid.NewGuid();
        var tenant = MakeTenant(); // no modules
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        _tenants.GetByIdAsync(tenant.Id, default).Returns(tenant);
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });

        var (membership, error, statusCode) = await _sut.JoinBySlugAsync(consumerId, "acme");

        Assert.Null(membership);
        Assert.Equal(403, statusCode);
    }

    // ── LeaveAsync / LeaveBySlugAsync (TASK-548) ──────────────────────────

    [Fact]
    public async Task LeaveAsync_no_membership_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (success, error, statusCode) = await _sut.LeaveAsync(consumerId, tenantId);

        Assert.False(success);
        Assert.Equal(404, statusCode);
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
    }

    [Fact]
    public async Task LeaveAsync_active_membership_sets_status_left_and_persists_without_touching_balance()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId,
            Status = LoyaltyMembershipStatus.Active, Balance = 42m,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);

        var (success, error, statusCode) = await _sut.LeaveAsync(consumerId, tenantId);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(LoyaltyMembershipStatus.Left, existing.Status);
        Assert.Equal(42m, existing.Balance); // balance/history untouched by leaving
        _loyalty.Received(1).UpdateMembership(existing);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task LeaveAsync_already_left_is_idempotent_without_redundant_write()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, Status = LoyaltyMembershipStatus.Left,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);

        var (success, error, statusCode) = await _sut.LeaveAsync(consumerId, tenantId);

        Assert.True(success);
        Assert.Null(error);
        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
        await _loyalty.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task LeaveBySlugAsync_unknown_slug_returns_404()
    {
        _tenants.GetBySlugAsync("ghost", default).ReturnsNull();

        var (success, error, statusCode) = await _sut.LeaveBySlugAsync(Guid.NewGuid(), "ghost");

        Assert.False(success);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task LeaveBySlugAsync_known_slug_delegates_to_LeaveAsync()
    {
        var consumerId = Guid.NewGuid();
        var tenant = MakeTenant("loyalty");
        _tenants.GetBySlugAsync("acme", default).Returns(tenant);
        var existing = new LoyaltyMembership
        {
            TenantId = tenant.Id, ConsumerAccountId = consumerId, Status = LoyaltyMembershipStatus.Active,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenant.Id, consumerId, default).Returns(existing);

        var (success, error, statusCode) = await _sut.LeaveBySlugAsync(consumerId, "acme");

        Assert.True(success);
        Assert.Equal(LoyaltyMembershipStatus.Left, existing.Status);
    }

    // ── JoinAsync — rejoin-after-leave reactivation (TASK-548) ────────────

    [Fact]
    public async Task JoinAsync_rejoining_a_left_membership_reactivates_it_and_preserves_balance()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId,
            Status = LoyaltyMembershipStatus.Left, Balance = 15m,
        };
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        Assert.Equal(LoyaltyMembershipStatus.Active, existing.Status);
        Assert.Equal(LoyaltyMembershipStatus.Active, membership.Status);
        Assert.Equal(15m, membership.Balance); // preserved across leave/rejoin, not reset
        _loyalty.Received(1).UpdateMembership(existing);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task JoinAsync_rejoining_an_already_active_membership_does_not_write()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId,
            Status = LoyaltyMembershipStatus.Active, Balance = 15m,
        };
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);

        await _sut.JoinAsync(consumerId, tenantId);

        _loyalty.DidNotReceive().UpdateMembership(Arg.Any<LoyaltyMembership>());
        await _loyalty.DidNotReceive().SaveChangesAsync(default);
    }

    /// <summary>TASK-627: rejoining a "left" membership must never assign/reset the entry tier,
    /// even when the tenant has a configured ladder — that branch reactivates a row that may
    /// already carry real tier history from before the consumer left, and entry-tier assignment
    /// is scoped to brand-new membership creation only (CreateMembershipCoreAsync/
    /// JoinAsStaffAsync), not this reactivation branch.</summary>
    [Fact]
    public async Task JoinAsync_rejoining_a_left_membership_does_not_touch_tier_even_with_ladder_configured()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var earnedTierId = Guid.NewGuid();
        var existing = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId,
            Status = LoyaltyMembershipStatus.Left, Balance = 15m,
            CurrentTierId = earnedTierId, CompositeScore = 7.5m,
        };
        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0 };
        _consumerAccounts.GetByIdAsync(consumerId, default)
            .Returns(new ConsumerAccount { Phone = "+380501234567", FullName = "X", IsActive = true });
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant("loyalty"));
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(existing);
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze });

        var (membership, error, statusCode) = await _sut.JoinAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(membership);
        // LoyaltyMembershipSummaryDto doesn't surface tier fields — assert against the entity
        // instance itself (the same object GetMembershipByTenantConsumerAsync returned, which
        // JoinAsync's rejoin branch mutates in place if it touches it at all).
        Assert.Equal(earnedTierId, existing.CurrentTierId); // untouched, not reset to bronze
        Assert.Equal(7.5m, existing.CompositeScore); // untouched
        await _loyalty.DidNotReceive().AddTierHistoryAsync(Arg.Any<LoyaltyTierChangeHistory>(), default);
        await _loyalty.DidNotReceive().GetTierLadderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Tier ladder — admin CRUD (TASK-615) ───────────────────────────────

    [Fact]
    public async Task GetTierLadderAsync_returns_tiers_ordered_by_service_from_repo()
    {
        var tenantId = Guid.NewGuid();
        var tiers = new List<LoyaltyTierDefinition>
        {
            new() { TenantId = tenantId, Name = "Bronze", SortOrder = 0, AccrualMultiplier = 1.0m },
            new() { TenantId = tenantId, Name = "Gold", SortOrder = 1, AccrualMultiplier = 1.5m, DiscountPercent = 10m },
        };
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(tiers);

        var result = await _sut.GetTierLadderAsync(tenantId);

        Assert.Equal(2, result.Count);
        Assert.Equal("Bronze", result[0].Name);
        Assert.Equal("Gold", result[1].Name);
        Assert.Equal(1.5m, result[1].AccrualMultiplier);
    }

    [Theory]
    [InlineData("", 0, 0, 1, 0)]          // empty name
    [InlineData("Gold", -1, 0, 1, 0)]     // negative SortOrder
    [InlineData("Gold", 0, -1, 1, 0)]     // negative MinCompositeScore
    [InlineData("Gold", 0, 0, -1, 0)]     // negative AccrualMultiplier
    [InlineData("Gold", 0, 0, 1, 150)]    // DiscountPercent out of range
    public async Task UpsertTierLadderAsync_invalid_row_returns_error(
        string name, int sortOrder, decimal minScore, decimal multiplier, decimal discount)
    {
        var (result, error) = await _sut.UpsertTierLadderAsync(Guid.NewGuid(),
            [new UpsertTierRequest(name, sortOrder, minScore, multiplier, discount)]);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpsertTierLadderAsync_duplicate_sortOrder_returns_error()
    {
        var (result, error) = await _sut.UpsertTierLadderAsync(Guid.NewGuid(),
        [
            new UpsertTierRequest("Bronze", 0, 0, 1, 0),
            new UpsertTierRequest("Silver", 0, 10, 1.2m, 5),
        ]);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpsertTierLadderAsync_no_existing_ladder_adds_all_tiers()
    {
        var tenantId = Guid.NewGuid();
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition>());

        var (result, error) = await _sut.UpsertTierLadderAsync(tenantId,
        [
            new UpsertTierRequest("Bronze", 0, 0, 1.0m, 0),
            new UpsertTierRequest("Gold", 1, 100, 1.5m, 10),
        ]);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        await _loyalty.Received(2).AddTierAsync(Arg.Any<LoyaltyTierDefinition>(), default);
        await _loyalty.Received(1).SaveChangesAsync(default);
    }

    /// <summary>
    /// A tier row whose SortOrder is unchanged between requests keeps its database Id (matched,
    /// not recreated) — so any LoyaltyMembership.CurrentTierId already pointing at it stays
    /// valid. See ILoyaltyService.UpsertTierLadderAsync's doc for the full rationale.
    /// </summary>
    [Fact]
    public async Task UpsertTierLadderAsync_matches_existing_row_by_sortOrder_and_preserves_id()
    {
        var tenantId = Guid.NewGuid();
        var existingGold = new LoyaltyTierDefinition
        {
            TenantId = tenantId, Name = "Gold", SortOrder = 1, AccrualMultiplier = 1.5m, DiscountPercent = 5m,
        };
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { existingGold });

        var (result, error) = await _sut.UpsertTierLadderAsync(tenantId,
        [
            new UpsertTierRequest("Gold Plus", 1, 200, 2.0m, 15), // same SortOrder, new values
        ]);

        Assert.Null(error);
        Assert.NotNull(result);
        var updated = Assert.Single(result!);
        Assert.Equal(existingGold.Id, updated.Id); // Id preserved
        Assert.Equal("Gold Plus", updated.Name);
        Assert.Equal(2.0m, updated.AccrualMultiplier);
        _loyalty.Received(1).UpdateTier(existingGold);
        await _loyalty.DidNotReceive().AddTierAsync(Arg.Any<LoyaltyTierDefinition>(), default);
    }

    [Fact]
    public async Task UpsertTierLadderAsync_removes_tiers_whose_sortOrder_is_no_longer_submitted()
    {
        var tenantId = Guid.NewGuid();
        var stale = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Stale", SortOrder = 5 };
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { stale });

        var (result, error) = await _sut.UpsertTierLadderAsync(tenantId,
            [new UpsertTierRequest("Bronze", 0, 0, 1, 0)]);

        Assert.Null(error);
        Assert.NotNull(result);
        _loyalty.Received(1).RemoveTier(stale);
    }

    // ── GetTierProgressAsync (TASK-615) ───────────────────────────────────

    [Fact]
    public async Task GetTierProgressAsync_no_membership_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (progress, error, statusCode) = await _sut.GetTierProgressAsync(consumerId, tenantId);

        Assert.Null(progress);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetTierProgressAsync_membership_without_tier_returns_default_multiplier_and_lowest_tier_as_next()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, CompositeScore = 20m,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);

        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0, MinCompositeScore = 50m };
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze });

        var (progress, error, statusCode) = await _sut.GetTierProgressAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(progress);
        Assert.Null(progress!.CurrentTierId);
        Assert.Equal(0m, progress.AccrualMultiplier);
        Assert.Equal(0m, progress.DiscountPercent);
        Assert.Equal("Bronze", progress.NextTierName);
        Assert.Equal(30m, progress.ScoreToNextTier); // 50 - 20
    }

    [Fact]
    public async Task GetTierProgressAsync_current_tier_reports_next_rung_gap()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0, MinCompositeScore = 0m, AccrualMultiplier = 1.0m };
        var gold = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Gold", SortOrder = 1, MinCompositeScore = 100m, AccrualMultiplier = 1.5m, DiscountPercent = 10m };
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, CompositeScore = 40m, CurrentTierId = bronze.Id,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze, gold });

        var (progress, error, statusCode) = await _sut.GetTierProgressAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(progress);
        Assert.Equal(bronze.Id, progress!.CurrentTierId);
        Assert.Equal("Bronze", progress.CurrentTierName);
        Assert.Equal("Gold", progress.NextTierName);
        Assert.Equal(60m, progress.ScoreToNextTier); // 100 - 40
    }

    [Fact]
    public async Task GetTierProgressAsync_top_tier_has_no_next_tier()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gold = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Gold", SortOrder = 0, AccrualMultiplier = 1.5m, DiscountPercent = 10m };
        var membership = new LoyaltyMembership
        {
            TenantId = tenantId, ConsumerAccountId = consumerId, CompositeScore = 500m, CurrentTierId = gold.Id,
        };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { gold });

        var (progress, error, statusCode) = await _sut.GetTierProgressAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(progress);
        Assert.Equal(1.5m, progress!.AccrualMultiplier);
        Assert.Equal(10m, progress.DiscountPercent);
        Assert.Null(progress.NextTierId);
        Assert.Null(progress.ScoreToNextTier);
    }

    // ── GetTierHistoryAsync (TASK-615) ────────────────────────────────────

    [Fact]
    public async Task GetTierHistoryAsync_no_membership_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (history, error, statusCode) = await _sut.GetTierHistoryAsync(consumerId, tenantId, 1, 50);

        Assert.Null(history);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetTierHistoryAsync_maps_paged_entries()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membership = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);

        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze" };
        var gold = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Gold" };
        var entries = new List<LoyaltyTierChangeHistory>
        {
            new()
            {
                TenantId = tenantId, MembershipId = membership.Id,
                FromTier = bronze, ToTier = gold, FromScore = 40m, ToScore = 110m,
            },
        };
        _loyalty.GetTierHistoryPagedAsync(tenantId, membership.Id, 1, 50, default).Returns((entries, 1));

        var (history, error, statusCode) = await _sut.GetTierHistoryAsync(consumerId, tenantId, 1, 50);

        Assert.Null(error);
        Assert.NotNull(history);
        var entry = Assert.Single(history!.Items);
        Assert.Equal("Bronze", entry.FromTierName);
        Assert.Equal("Gold", entry.ToTierName);
        Assert.Equal(1, history.TotalCount);
    }

    // ── GetTierLadderForConsumerAsync (TASK-626) ──────────────────────────

    [Fact]
    public async Task GetTierLadderForConsumerAsync_no_membership_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();

        var (ladder, error, statusCode) = await _sut.GetTierLadderForConsumerAsync(consumerId, tenantId);

        Assert.Null(ladder);
        Assert.Equal(404, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetTierLadderForConsumerAsync_active_member_gets_full_ladder_ordered_by_sortOrder()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membership = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);

        var bronze = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Bronze", SortOrder = 0, AccrualMultiplier = 1.0m };
        var gold = new LoyaltyTierDefinition { TenantId = tenantId, Name = "Gold", SortOrder = 1, AccrualMultiplier = 1.5m, DiscountPercent = 10m };
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition> { bronze, gold });

        var (ladder, error, statusCode) = await _sut.GetTierLadderForConsumerAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(ladder);
        Assert.Equal(2, ladder!.Count);
        Assert.Equal("Bronze", ladder[0].Name);
        Assert.Equal("Gold", ladder[1].Name);
        Assert.Equal(1.5m, ladder[1].AccrualMultiplier);
        Assert.Equal(10m, ladder[1].DiscountPercent);
    }

    [Fact]
    public async Task GetTierLadderForConsumerAsync_no_tiers_configured_returns_empty_list_not_null()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membership = new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId };
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).Returns(membership);
        _loyalty.GetTierLadderAsync(tenantId, default).Returns(new List<LoyaltyTierDefinition>());

        var (ladder, error, statusCode) = await _sut.GetTierLadderForConsumerAsync(consumerId, tenantId);

        Assert.Null(error);
        Assert.NotNull(ladder);
        Assert.Empty(ladder!);
    }
}
