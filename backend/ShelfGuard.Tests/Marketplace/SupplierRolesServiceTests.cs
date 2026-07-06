using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-306: custom supplier staff roles are scoped to the calling tenant, mirroring
/// ProviderRolesService's validation but never operating outside tenantId.
/// </summary>
public sealed class SupplierRolesServiceTests
{
    private readonly ISupplierRolesRepository _repo = Substitute.For<ISupplierRolesRepository>();
    private readonly SupplierRolesService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();

    public SupplierRolesServiceTests() => _sut = new SupplierRolesService(_repo);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesRoleScopedToTenant()
    {
        _repo.DisplayNameExistsAsync(_tenantId, "Support", Arg.Any<CancellationToken>())
             .Returns(false);

        var request = new CreateSupplierRoleRequest(
            "Support", AppRoles.SupplierAdmin, [SupplierPermissions.ClientReviews]);

        var (role, error) = await _sut.CreateAsync(_tenantId, request);

        Assert.Null(error);
        Assert.NotNull(role);
        Assert.Equal("Support", role!.DisplayName);
        Assert.Equal(new[] { SupplierPermissions.ClientReviews }, role.Permissions);
        await _repo.Received(1).AddAsync(
            Arg.Is<SupplierRole>(r => r.TenantId == _tenantId && r.DisplayName == "Support"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UnknownPermission_ReturnsValidationError_DoesNotSave()
    {
        var request = new CreateSupplierRoleRequest(
            "Support", AppRoles.SupplierAdmin, ["not_a_real_permission"]);

        var (role, error) = await _sut.CreateAsync(_tenantId, request);

        Assert.Null(role);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierRole>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_MissingDisplayName_ReturnsValidationError()
    {
        var request = new CreateSupplierRoleRequest("   ", AppRoles.SupplierAdmin, []);

        var (role, error) = await _sut.CreateAsync(_tenantId, request);

        Assert.Null(role);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_InvalidBaseRole_ReturnsValidationError()
    {
        var request = new CreateSupplierRoleRequest("Support", "provider_admin", []);

        var (role, error) = await _sut.CreateAsync(_tenantId, request);

        Assert.Null(role);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_DuplicateDisplayName_ReturnsError()
    {
        _repo.DisplayNameExistsAsync(_tenantId, "Support", Arg.Any<CancellationToken>())
             .Returns(true);

        var request = new CreateSupplierRoleRequest("Support", AppRoles.SupplierAdmin, []);

        var (role, error) = await _sut.CreateAsync(_tenantId, request);

        Assert.Null(role);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ExistingRole_UpdatesFields()
    {
        var role = SupplierRole.Create(_tenantId, "Old Name", AppRoles.SupplierAdmin, []);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _repo.DisplayNameExistsAsync(_tenantId, "New Name", Arg.Any<CancellationToken>()).Returns(false);

        var request = new UpdateSupplierRoleRequest(
            "New Name", AppRoles.SupplierAdmin, [SupplierPermissions.TaskBoard]);

        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(error);
        Assert.Equal("New Name", dto!.DisplayName);
        Assert.Equal(new[] { SupplierPermissions.TaskBoard }, role.Permissions);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RoleNotFound_ReturnsError()
    {
        _repo.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((SupplierRole?)null);

        var request = new UpdateSupplierRoleRequest("X", AppRoles.SupplierAdmin, []);
        var (dto, error) = await _sut.UpdateAsync(_tenantId, Guid.NewGuid(), request);

        Assert.Null(dto);
        Assert.Equal("Role not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_SystemRole_ReturnsError_DoesNotSave()
    {
        var role = SupplierRole.Create(_tenantId, "System Role", AppRoles.SupplierAdmin, []);
        typeof(SupplierRole).GetProperty(nameof(SupplierRole.IsSystem))!
            .SetValue(role, true); // simulate a seeded system role
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var request = new UpdateSupplierRoleRequest("New Name", AppRoles.SupplierAdmin, []);
        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(dto);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_UnassignedRole_Deletes()
    {
        var role = SupplierRole.Create(_tenantId, "Support", AppRoles.SupplierAdmin, []);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _repo.IsAssignedToAnyUserAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(false);

        var (success, error) = await _sut.DeleteAsync(_tenantId, role.Id);

        Assert.True(success);
        Assert.Null(error);
        _repo.Received(1).Remove(role);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RoleAssignedToStaff_ReturnsError_DoesNotDelete()
    {
        var role = SupplierRole.Create(_tenantId, "Support", AppRoles.SupplierAdmin, []);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _repo.IsAssignedToAnyUserAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(true);

        var (success, error) = await _sut.DeleteAsync(_tenantId, role.Id);

        Assert.False(success);
        Assert.NotNull(error);
        _repo.DidNotReceive().Remove(Arg.Any<SupplierRole>());
    }
}
