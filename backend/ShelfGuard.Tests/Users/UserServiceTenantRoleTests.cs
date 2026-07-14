using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Users;

/// <summary>
/// TASK-346: UserService.AssignTenantRoleAsync — cross-tenant belonging check and archived-role
/// rejection. The controller gates this AtLeastEnterpriseAdmin-only with no capability bypass;
/// these tests cover the service-layer validation only.
/// </summary>
public sealed class UserServiceTenantRoleTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ILegalEntityService _legalEntities = Substitute.For<ILegalEntityService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly UserService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();

    public UserServiceTenantRoleTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_ValidActiveRoleSameTenant_Assigns()
    {
        var target = MakeUser(_tenantId);
        var role = TenantRole.Create(_tenantId, "HR", [], null);
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _tenantRoles.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, role.Id);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(role.Id, target.TenantRoleId);
        _users.Received(1).Update(target);
        await _users.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_NullClearsAssignment()
    {
        var target = MakeUser(_tenantId);
        target.SetTenantRole(Guid.NewGuid());
        _users.GetByIdAsync(target.Id, default).Returns(target);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, null);

        Assert.True(success);
        Assert.Null(error);
        Assert.Null(target.TenantRoleId);
        await _tenantRoles.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignTenantRoleAsync_TargetUserNotFound_ReturnsError()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), default).Returns((User?)null);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_TargetUserBelongsToDifferentTenant_ReturnsNotFound()
    {
        var target = MakeUser(Guid.NewGuid()); // different tenant
        _users.GetByIdAsync(target.Id, default).Returns(target);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    // The critical anti-enumeration case: a tenantRoleId that belongs to a DIFFERENT tenant
    // must look EXACTLY like "not found" — never reveal that the id exists elsewhere.
    [Fact]
    public async Task AssignTenantRoleAsync_RoleBelongsToDifferentTenant_ReturnsNotFound_NotForbidden()
    {
        var target = MakeUser(_tenantId);
        _users.GetByIdAsync(target.Id, default).Returns(target);
        // Repository is tenant-scoped: a cross-tenant id simply isn't found.
        _tenantRoles.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TenantRole?)null);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("TenantRole not found.", error);
        Assert.Null(target.TenantRoleId);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_ArchivedRole_ReturnsError_DoesNotAssign()
    {
        var target = MakeUser(_tenantId);
        var role = TenantRole.Create(_tenantId, "Archived", [], null);
        role.Deactivate();
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _tenantRoles.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var (success, error) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, role.Id);

        Assert.False(success);
        Assert.Equal("Cannot assign an archived TenantRole.", error);
        Assert.Null(target.TenantRoleId);
        await _users.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_ReassignmentReplacesPreviousRole()
    {
        var oldRoleId = Guid.NewGuid();
        var target = MakeUser(_tenantId);
        target.SetTenantRole(oldRoleId);
        var newRole = TenantRole.Create(_tenantId, "Закупка", [], null);
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _tenantRoles.GetByIdAsync(_tenantId, newRole.Id, Arg.Any<CancellationToken>()).Returns(newRole);

        var (success, _) = await _sut.AssignTenantRoleAsync(_tenantId, target.Id, newRole.Id);

        Assert.True(success);
        Assert.Equal(newRole.Id, target.TenantRoleId);
    }

    private static User MakeUser(Guid tenantId) =>
        User.Create(tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", "staff");
}
