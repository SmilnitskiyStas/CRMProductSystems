using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Admin;
using ShelfGuard.Application.Features.Admin.Dtos;
using ShelfGuard.Application.Services;
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
