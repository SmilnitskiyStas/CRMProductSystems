using NSubstitute;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Provider;

/// <summary>
/// TASK-289 (ADR-016): the provider tenant-creation wizard must onboard supplier
/// tenants the same way TenantAdminService already does, and CreateTenantUserAsync
/// must validate the requested role against the target tenant's business_type.
/// </summary>
public sealed class ProviderServiceTests
{
    private readonly ITenantRepository      _tenants = Substitute.For<ITenantRepository>();
    private readonly IActivityLogRepository _logs     = Substitute.For<IActivityLogRepository>();
    private readonly IJwtService            _jwt      = Substitute.For<IJwtService>();
    private readonly IUserRepository        _users    = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher        _hasher   = Substitute.For<IPasswordHasher>();
    private readonly ProviderService _sut;

    public ProviderServiceTests() =>
        _sut = new ProviderService(_tenants, _logs, _jwt, _users, _hasher);

    // ── CreateTenantAsync — supplier onboarding hook (TASK-289) ────────────────

    [Fact]
    public async Task CreateTenant_SupplierBusinessType_CreatesOwnerManagedSupplierPair()
    {
        Tenant? persistedTenant = null;
        _tenants.When(r => r.AddPendingAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>()))
                .Do(ci => persistedTenant = ci.Arg<Tenant>());

        var req = new CreateTenantRequest(
            Name:         "Fresh Foods Ltd",
            Slug:         "fresh-foods-2",
            BusinessType: "supplier",
            Plan:         "basic",
            Modules:      null);

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.Equal("supplier", tenant!.BusinessType);

        await _tenants.Received(1).AddSupplierAsync(
            Arg.Is<Supplier>(s => s.TenantId == persistedTenant!.Id && s.Name == "Fresh Foods Ltd"),
            Arg.Any<CancellationToken>());
        await _tenants.Received(1).AddSupplierProfileAsync(
            Arg.Is<SupplierProfile>(p =>
                p.TenantId == persistedTenant!.Id &&
                p.IsOwnerManaged &&
                !p.IsPublic),
            Arg.Any<CancellationToken>());

        // Single transaction — exactly one SaveChanges
        await _tenants.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTenant_NonSupplierBusinessType_DoesNotCreateSupplierPair()
    {
        var req = new CreateTenantRequest(
            Name:         "Retail Shop",
            Slug:         "retail-shop-2",
            BusinessType: "retail",
            Plan:         "basic",
            Modules:      null);

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        await _tenants.DidNotReceive().AddSupplierAsync(Arg.Any<Supplier>(), Arg.Any<CancellationToken>());
        await _tenants.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    // ── CreateTenantAsync — primary supplier category (TASK-665) ───────────────

    [Fact]
    public async Task CreateTenant_Beauty_EmptyModules_IgnoresSupplierCategory()
    {
        var req = new CreateTenantRequest(
            "Salon", "salon-beauty", "beauty", "basic", Array.Empty<string>(),
            SupplierCategory: "not_even_valid");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.Equal("beauty", tenant!.BusinessType);
        Assert.Empty(tenant.Modules);
        await _tenants.DidNotReceive().AddSupplierAsync(Arg.Any<Supplier>(), Arg.Any<CancellationToken>());
        await _tenants.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTenant_Beauty_NullOrNonEmptyModules_StillEmpty()
    {
        var req = new CreateTenantRequest(
            "Salon", "salon-beauty-2", "beauty", "basic", new[] { "inventory" });

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.Empty(tenant!.Modules);
    }

    [Fact]
    public async Task Impersonate_BeautyTenant_ReturnsError_NoToken()
    {
        var beauty = Tenant.Create("Salon", "salon-imp");
        beauty.UpdateBusinessType("beauty");
        _tenants.GetByIdAsync(beauty.Id, Arg.Any<CancellationToken>()).Returns(beauty);

        var (response, error) = await _sut.ImpersonateAsync(Guid.NewGuid(), "p@x.com", beauty.Id, default);

        Assert.Null(response);
        Assert.Equal("Impersonation is not available for beauty tenants.", error);
        _jwt.DidNotReceiveWithAnyArgs().GenerateImpersonationToken(default, default!, default, default, default, default);
    }

    private Tenant StubTenant(string type, bool active = true)
    {
        var t = Tenant.Create("Salon", "salon-" + Guid.NewGuid().ToString("N")[..8]);
        Assert.Null(t.UpdateBusinessType(type));
        if (!active) t.Deactivate();
        _tenants.GetByIdAsync(t.Id, Arg.Any<CancellationToken>()).Returns(t);
        return t;
    }

    [Fact]
    public async Task Impersonate_Beauty_WritesNoActivityLog()
    {
        var beauty = StubTenant("beauty");
        await _sut.ImpersonateAsync(Guid.NewGuid(), "p@x.com", beauty.Id, default);
        await _logs.DidNotReceiveWithAnyArgs().LogAsync(default!, default);
        await _logs.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Impersonate_NotFound_And_Deactivated_Win_Over_Beauty()
    {
        var (_, nf) = await _sut.ImpersonateAsync(Guid.NewGuid(), "p@x.com", Guid.NewGuid(), default);
        Assert.Equal("Tenant not found.", nf);

        var inactiveBeauty = StubTenant("beauty", active: false);
        var (_, deact) = await _sut.ImpersonateAsync(Guid.NewGuid(), "p@x.com", inactiveBeauty.Id, default);
        Assert.Equal("Tenant is deactivated.", deact);
    }

    [Fact]
    public async Task Impersonate_UppercaseBeautyFromDb_IsBlocked()
    {
        // Legacy/raw value: the guard must be case-insensitive, not rely on UpdateBusinessType lowercasing.
        var beauty = StubTenant("beauty");
        typeof(Tenant).GetProperty(nameof(Tenant.BusinessType))!.SetValue(beauty, "Beauty");
        var (response, error) = await _sut.ImpersonateAsync(Guid.NewGuid(), "p@x.com", beauty.Id, default);
        Assert.Null(response);
        Assert.Equal("Impersonation is not available for beauty tenants.", error);
    }

    [Fact]
    public async Task CreateTenantUser_Beauty_IsRejected_NoUserCreated()
    {
        var beauty = StubTenant("beauty");
        var (user, error) = await _sut.CreateTenantUserAsync(
            beauty.Id, new CreateTenantUserRequest("Owner", "o@salon.test", "SecurePass123!x", AppRoles.EnterpriseAdmin), default);

        Assert.Null(user);
        Assert.Equal("Users for beauty tenants are managed in Beauty CRM.", error);
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task UpdateModules_Beauty_OnlyEmptyAccepted()
    {
        var beauty = StubTenant("beauty");

        var bad = await _sut.UpdateModulesAsync(beauty.Id, ["inventory"], default);
        Assert.Equal("Beauty tenants cannot have ShelfGuard modules.", bad);
        await _tenants.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        var ok = await _sut.UpdateModulesAsync(beauty.Id, [], default);
        Assert.Null(ok);
        Assert.Empty(beauty.GetModules());
    }

    [Fact]
    public async Task CreateTenant_UppercaseBeauty_TreatedAsBeauty_ModulesEmpty()
    {
        var (tenant, error) = await _sut.CreateTenantAsync(
            new CreateTenantRequest("Salon", "salon-upper", "Beauty", "basic", ["inventory"]), default);
        Assert.Null(error);
        Assert.Equal("beauty", tenant!.BusinessType);
        Assert.Empty(tenant.Modules);
    }

    [Theory]
    [InlineData("-bad")]
    [InlineData("has_underscore")]
    [InlineData("with space")]
    public async Task CreateTenant_Beauty_InvalidSlug_Rejected(string slug)
    {
        var (tenant, error) = await _sut.CreateTenantAsync(
            new CreateTenantRequest("Salon", slug, "beauty", "basic", null), default);
        Assert.Null(tenant);
        Assert.StartsWith("Invalid slug for a beauty tenant", error);
    }

    [Fact]
    public async Task CreateTenant_Beauty_LongSlugOrName_Rejected()
    {
        var (t1, e1) = await _sut.CreateTenantAsync(
            new CreateTenantRequest("Salon", new string('a', 64), "beauty", "basic", null), default);
        Assert.Null(t1);
        Assert.StartsWith("Invalid slug", e1);

        var (t2, e2) = await _sut.CreateTenantAsync(
            new CreateTenantRequest(new string('n', 151), "salon-long", "beauty", "basic", null), default);
        Assert.Null(t2);
        Assert.StartsWith("Name must be at most 150", e2);
    }

    [Fact]
    public async Task CreateTenant_NonBeauty_NullModules_UseDefaults_ExplicitEmpty_StaysEmpty()
    {
        var (def, e1) = await _sut.CreateTenantAsync(
            new CreateTenantRequest("Shop", "shop-defaults", "retail", "basic", null), default);
        Assert.Null(e1);
        Assert.NotEmpty(def!.Modules);

        var (empty, e2) = await _sut.CreateTenantAsync(
            new CreateTenantRequest("Shop", "shop-empty", "retail", "basic", []), default);
        Assert.Null(e2);
        Assert.Empty(empty!.Modules);
    }

    [Fact]
    public async Task CreateTenant_SupplierWithValidCategory_SeedsSingleCategoryOnProfile()
    {
        SupplierProfile? persistedProfile = null;
        _tenants.When(r => r.AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>()))
                .Do(ci => persistedProfile = ci.Arg<SupplierProfile>());

        var req = new CreateTenantRequest(
            "Fresh Foods Ltd", "fresh-foods-cat", "supplier", "basic", null,
            SupplierCategory: "food");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.NotNull(persistedProfile);
        Assert.Equal("[\"food\"]", persistedProfile!.Categories);
    }

    [Fact]
    public async Task CreateTenant_SupplierWithUnknownCategory_ReturnsError_PersistsNothing()
    {
        var req = new CreateTenantRequest(
            "Fresh Foods Ltd", "fresh-foods-badcat", "supplier", "basic", null,
            SupplierCategory: "spaceship_fuel");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(tenant);
        Assert.NotNull(error);
        Assert.Contains("spaceship_fuel", error);
        await _tenants.DidNotReceive().AddPendingAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _tenants.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTenant_NonSupplierWithCategory_IgnoresIt_NoError()
    {
        var req = new CreateTenantRequest(
            "Retail Shop", "retail-shop-cat", "retail", "basic", null,
            SupplierCategory: "not_even_valid");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        await _tenants.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    // ── SetSupplierCategoryAsync (TASK-665) ───────────────────────────────────

    [Fact]
    public async Task SetSupplierCategory_ValidCategory_WritesSingleElementAndSaves()
    {
        var tenant = Tenant.Create("Fresh Foods", "fresh-foods-set");
        tenant.UpdateBusinessType("supplier");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var profile = new SupplierProfile { TenantId = tenant.Id, IsOwnerManaged = true };
        _tenants.GetOwnerManagedSupplierProfileAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(profile);

        var error = await _sut.SetSupplierCategoryAsync(tenant.Id, "auto_parts", default);

        Assert.Null(error);
        Assert.Equal("[\"auto_parts\"]", profile.Categories);
        await _tenants.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSupplierCategory_BlankCategory_ClearsIt()
    {
        var tenant = Tenant.Create("Fresh Foods", "fresh-foods-clear");
        tenant.UpdateBusinessType("supplier");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var profile = new SupplierProfile { TenantId = tenant.Id, IsOwnerManaged = true, Categories = "[\"food\"]" };
        _tenants.GetOwnerManagedSupplierProfileAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(profile);

        var error = await _sut.SetSupplierCategoryAsync(tenant.Id, "  ", default);

        Assert.Null(error);
        Assert.Null(profile.Categories);
        await _tenants.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSupplierCategory_UnknownCategory_ReturnsError_DoesNotSave()
    {
        var tenant = Tenant.Create("Fresh Foods", "fresh-foods-set-bad");
        tenant.UpdateBusinessType("supplier");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var error = await _sut.SetSupplierCategoryAsync(tenant.Id, "nope", default);

        Assert.NotNull(error);
        Assert.Contains("nope", error);
        await _tenants.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSupplierCategory_NonSupplierTenant_ReturnsError()
    {
        var tenant = Tenant.Create("Retail Shop", "retail-set");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var error = await _sut.SetSupplierCategoryAsync(tenant.Id, "food", default);

        Assert.Equal("Tenant is not a supplier.", error);
        await _tenants.DidNotReceive().GetOwnerManagedSupplierProfileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSupplierCategory_TenantNotFound_ReturnsNotFound()
    {
        _tenants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var error = await _sut.SetSupplierCategoryAsync(Guid.NewGuid(), "food", default);

        Assert.Equal("Tenant not found.", error);
    }

    [Fact]
    public async Task SetSupplierCategory_NoProfile_ReturnsError()
    {
        var tenant = Tenant.Create("Fresh Foods", "fresh-foods-noprofile");
        tenant.UpdateBusinessType("supplier");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenants.GetOwnerManagedSupplierProfileAsync(tenant.Id, Arg.Any<CancellationToken>())
                .Returns((SupplierProfile?)null);

        var error = await _sut.SetSupplierCategoryAsync(tenant.Id, "food", default);

        Assert.NotNull(error);
        Assert.Contains("profile not found", error, StringComparison.OrdinalIgnoreCase);
        await _tenants.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── CreateTenantUserAsync — role validation against business_type (TASK-289) ──

    [Fact]
    public async Task CreateTenantUser_SupplierTenant_SupplierAdminRole_Succeeds()
    {
        var tenant = Tenant.Create("Fresh Foods Ltd", "fresh-foods-3");
        tenant.UpdateBusinessType("supplier");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantUserRequest(
            FullName: "Owner",
            Email:    "owner@freshfoods.com",
            Password: "SecurePass123",
            Role:     AppRoles.SupplierAdmin);

        var (user, error) = await _sut.CreateTenantUserAsync(tenant.Id, req, default);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.Equal(AppRoles.SupplierAdmin, user!.Role);
    }

    [Fact]
    public async Task CreateTenantUser_NonSupplierTenant_SupplierAdminRole_IsRejected()
    {
        var tenant = Tenant.Create("Retail Shop", "retail-shop-3");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var req = new CreateTenantUserRequest(
            FullName: "Someone",
            Email:    "someone@retailshop.com",
            Password: "SecurePass123",
            Role:     AppRoles.SupplierAdmin);

        var (user, error) = await _sut.CreateTenantUserAsync(tenant.Id, req, default);

        Assert.Null(user);
        Assert.NotNull(error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    // ── CreateTenantUserAsync — inactive tenant guard (BUG-014) ────────────────

    [Fact]
    public async Task CreateTenantUser_InactiveTenant_IsRejected()
    {
        var tenant = Tenant.Create("Platform Marketplace", "platform-marketplace");
        tenant.UpdateBusinessType("supplier");
        tenant.Deactivate();
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var req = new CreateTenantUserRequest(
            FullName: "Someone",
            Email:    "someone@platform.com",
            Password: "SecurePass123",
            Role:     AppRoles.SupplierAdmin);

        var (user, error) = await _sut.CreateTenantUserAsync(tenant.Id, req, default);

        Assert.Null(user);
        Assert.NotNull(error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }
}
