using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Admin;
using ShelfGuard.Application.Features.Admin.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Admin;

public sealed class TenantAdminServiceTests
{
    private readonly ITenantAdminRepository _repo = Substitute.For<ITenantAdminRepository>();
    private readonly IPasswordHasher _hasher      = Substitute.For<IPasswordHasher>();
    private readonly TenantAdminService _sut;

    public TenantAdminServiceTests()
    {
        _sut = new TenantAdminService(_repo, _hasher);

        // Default: no usage counts (zero is fine for logic tests)
        _repo.CountUsersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
        _repo.CountStoresAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
        _repo.CountProductsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
        _repo.CountSalesLast30DaysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);
    }

    // ── GetAllTenants_ReturnsAll ────────────────────────────────────────────

    [Fact]
    public async Task GetAllTenants_ReturnsAll()
    {
        var tenants = new List<Tenant>
        {
            MakeTenant("Alpha", "alpha"),
            MakeTenant("Beta",  "beta"),
            MakeTenant("Gamma", "gamma"),
        };
        _repo.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(tenants);

        var result = await _sut.GetAllTenantsAsync(default);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, t => t.Name == "Alpha");
        Assert.Contains(result, t => t.Name == "Beta");
        Assert.Contains(result, t => t.Name == "Gamma");
    }

    // ── CreateTenant_DuplicateSlug_Returns409 ──────────────────────────────

    [Fact]
    public async Task CreateTenant_DuplicateSlug_ReturnsConflictError()
    {
        _repo.SlugExistsAsync("acme", Arg.Any<CancellationToken>()).Returns(true);

        var req = new CreateTenantRequest(
            Name:          "Acme Corp",
            Slug:          "acme",
            Plan:          "basic",
            AdminEmail:    "admin@acme.com",
            AdminFullName: "Admin User",
            AdminPassword: "SecurePass123");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(tenant);
        Assert.NotNull(error);
        Assert.Contains("already taken", error, StringComparison.OrdinalIgnoreCase);

        // Ensure nothing was persisted
        await _repo.DidNotReceive().AddTenantAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── CreateTenant_DefaultModules_FollowBusinessType (TASK-208) ──────────

    [Fact]
    public async Task CreateTenant_NoBusinessType_DefaultsToRetailModules()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name:          "Acme Corp",
            Slug:          "acme2",
            Plan:          "basic",
            AdminEmail:    "admin@acme2.com",
            AdminFullName: "Admin User",
            AdminPassword: "SecurePass123");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.Equal("retail", tenant.BusinessType);
        Assert.Equal(new[] { "inventory", "procurement", "pos" }, tenant.Modules);
    }

    [Fact]
    public async Task CreateTenant_ExplicitBusinessType_AppliesMatchingDefaultModules()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name:          "Joe's Garage",
            Slug:          "joes-garage",
            Plan:          "basic",
            AdminEmail:    "admin@joesgarage.com",
            AdminFullName: "Admin User",
            AdminPassword: "SecurePass123",
            BusinessType:  "auto_service");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.Equal("auto_service", tenant.BusinessType);
        Assert.Equal(new[] { "auto_service", "procurement" }, tenant.Modules);
    }

    [Fact]
    public async Task CreateTenant_InvalidBusinessType_ReturnsError()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name:          "Acme Corp",
            Slug:          "acme3",
            Plan:          "basic",
            AdminEmail:    "admin@acme3.com",
            AdminFullName: "Admin User",
            AdminPassword: "SecurePass123",
            BusinessType:  "spaceship_repair");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(tenant);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddTenantAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    // ── CreateTenant — supplier self-service onboarding (TASK-283, ADR-016) ──

    [Fact]
    public async Task CreateTenant_SupplierBusinessType_CreatesSupplierPairAndSupplierAdmin()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        Tenant? persistedTenant = null;
        _repo.When(r => r.AddTenantAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>()))
             .Do(ci => persistedTenant = ci.Arg<Tenant>());

        var req = new CreateTenantRequest(
            Name:          "Fresh Foods Ltd",
            Slug:          "fresh-foods",
            Plan:          "basic",
            AdminEmail:    "owner@freshfoods.com",
            AdminFullName: "Owner",
            AdminPassword: "SecurePass123",
            BusinessType:  "supplier");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.Equal("supplier", tenant.BusinessType);
        Assert.Equal(new[] { "marketplace_supplier" }, tenant.Modules);

        // First user gets the supplier_admin role, not enterprise_admin
        await _repo.Received(1).AddUserAsync(
            Arg.Is<User>(u => u.Role == AppRoles.SupplierAdmin && u.TenantId == persistedTenant!.Id),
            Arg.Any<CancellationToken>());

        // Supplier + owner-managed hidden profile created for the new tenant
        await _repo.Received(1).AddSupplierAsync(
            Arg.Is<Supplier>(s => s.TenantId == persistedTenant!.Id && s.Name == "Fresh Foods Ltd"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddSupplierProfileAsync(
            Arg.Is<SupplierProfile>(p =>
                p.TenantId == persistedTenant!.Id &&
                p.IsOwnerManaged &&
                !p.IsPublic),
            Arg.Any<CancellationToken>());

        // Single transaction — exactly one SaveChanges
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTenant_NonSupplierBusinessType_DoesNotCreateSupplierPair()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name:          "Retail Shop",
            Slug:          "retail-shop",
            Plan:          "basic",
            AdminEmail:    "admin@retailshop.com",
            AdminFullName: "Admin",
            AdminPassword: "SecurePass123",
            BusinessType:  "retail");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        await _repo.Received(1).AddUserAsync(
            Arg.Is<User>(u => u.Role == AppRoles.EnterpriseAdmin),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddSupplierAsync(Arg.Any<Supplier>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    // ── CreateTenant — primary supplier category (TASK-665) ───────────────────

    [Fact]
    public async Task CreateTenant_SupplierWithValidCategory_SeedsSingleCategoryOnProfile()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        SupplierProfile? persistedProfile = null;
        _repo.When(r => r.AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>()))
             .Do(ci => persistedProfile = ci.Arg<SupplierProfile>());

        var req = new CreateTenantRequest(
            Name: "Fresh Foods Ltd", Slug: "fresh-foods-cat", Plan: "basic",
            AdminEmail: "owner@ff.com", AdminFullName: "Owner", AdminPassword: "SecurePass123",
            BusinessType: "supplier", SupplierCategory: "medical");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        Assert.NotNull(persistedProfile);
        Assert.Equal("[\"medical\"]", persistedProfile!.Categories);
    }

    [Fact]
    public async Task CreateTenant_SupplierWithUnknownCategory_ReturnsError_PersistsNothing()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name: "Fresh Foods Ltd", Slug: "fresh-foods-badcat", Plan: "basic",
            AdminEmail: "owner@ff.com", AdminFullName: "Owner", AdminPassword: "SecurePass123",
            BusinessType: "supplier", SupplierCategory: "spaceship_fuel");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(tenant);
        Assert.NotNull(error);
        Assert.Contains("spaceship_fuel", error);
        await _repo.DidNotReceive().AddTenantAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTenant_NonSupplierWithCategory_IgnoresIt()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var req = new CreateTenantRequest(
            Name: "Retail Shop", Slug: "retail-shop-cat", Plan: "basic",
            AdminEmail: "admin@rs.com", AdminFullName: "Admin", AdminPassword: "SecurePass123",
            BusinessType: "retail", SupplierCategory: "not_even_valid");

        var (tenant, error) = await _sut.CreateTenantAsync(req, default);

        Assert.Null(error);
        Assert.NotNull(tenant);
        await _repo.DidNotReceive().AddSupplierProfileAsync(Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    // ── UpdatePlan_InvalidPlan_ReturnsError ────────────────────────────────

    [Fact]
    public async Task UpdatePlan_InvalidPlan_ReturnsError()
    {
        var tenant = MakeTenant("Acme", "acme");
        _repo.GetTenantByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var (result, error) = await _sut.UpdatePlanAsync(tenant.Id, "premium_gold", default);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("premium_gold", error, StringComparison.OrdinalIgnoreCase);

        // SaveChanges must NOT have been called
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Deactivate_SetsIsActiveFalse ───────────────────────────────────────

    [Fact]
    public async Task Deactivate_SetsIsActiveFalse()
    {
        var tenant = MakeTenant("Acme", "acme");
        Assert.True(tenant.IsActive); // sanity check

        _repo.GetTenantByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var (result, error) = await _sut.DeactivateAsync(tenant.Id, default);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result.IsActive);

        // Verify the entity was mutated and saved
        Assert.False(tenant.IsActive);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Tenant MakeTenant(string name, string slug)
    {
        var tenant = Tenant.Create(name, slug);
        return tenant;
    }
}
