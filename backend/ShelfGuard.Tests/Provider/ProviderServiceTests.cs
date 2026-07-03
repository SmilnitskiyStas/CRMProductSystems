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
}
