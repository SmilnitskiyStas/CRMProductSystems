using NSubstitute;
using ShelfGuard.Application.Features.TenantRoles;
using ShelfGuard.Application.Features.TenantRoles.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.TenantRoles;

/// <summary>
/// TASK-346: TenantRole template CRUD. Every operation is scoped to tenantId; Capabilities
/// must validate against TenantRoleCapabilities.All (including the reused
/// TenantUserPermissions.LegalEntitiesManage key); Deactivate archives, never hard-deletes.
/// </summary>
public sealed class TenantRoleServiceTests
{
    private readonly ITenantRoleRepository _repo = Substitute.For<ITenantRoleRepository>();
    private readonly TenantRoleService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();

    public TenantRoleServiceTests() => _sut = new TenantRoleService(_repo);

    // ── Create ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesTemplateScopedToTenant()
    {
        _repo.GetByNameAsync(_tenantId, "HR", Arg.Any<CancellationToken>()).Returns((TenantRole?)null);

        var request = new CreateTenantRoleRequest("HR", [TenantRoleCapabilities.UsersManage]);

        var (role, error) = await _sut.CreateAsync(_tenantId, Guid.NewGuid(), request);

        Assert.Null(error);
        Assert.NotNull(role);
        Assert.Equal("HR", role!.Name);
        Assert.Equal(new[] { TenantRoleCapabilities.UsersManage }, role.Capabilities.ToArray());
        Assert.True(role.IsActive);
        await _repo.Received(1).AddAsync(
            Arg.Is<TenantRole>(r => r.TenantId == _tenantId && r.Name == "HR"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ReusedLegalEntitiesManageKey_IsAccepted()
    {
        _repo.GetByNameAsync(_tenantId, "Бухгалтер", Arg.Any<CancellationToken>()).Returns((TenantRole?)null);

        var request = new CreateTenantRoleRequest("Бухгалтер",
            [TenantRoleCapabilities.AnalyticsView, TenantUserPermissions.LegalEntitiesManage]);

        var (role, error) = await _sut.CreateAsync(_tenantId, null, request);

        Assert.Null(error);
        Assert.Contains(TenantUserPermissions.LegalEntitiesManage, role!.Capabilities);
    }

    [Fact]
    public async Task CreateAsync_UnknownCapability_ReturnsValidationError_DoesNotSave()
    {
        var request = new CreateTenantRoleRequest("HR", ["not_a_real_capability"]);

        var (role, error) = await _sut.CreateAsync(_tenantId, null, request);

        Assert.Null(role);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<TenantRole>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_MissingName_ReturnsValidationError()
    {
        var request = new CreateTenantRoleRequest("   ", []);

        var (role, error) = await _sut.CreateAsync(_tenantId, null, request);

        Assert.Null(role);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_DuplicateActiveName_ReturnsError_DoesNotSave()
    {
        _repo.GetByNameAsync(_tenantId, "HR", Arg.Any<CancellationToken>())
             .Returns(TenantRole.Create(_tenantId, "HR", [], null));

        var request = new CreateTenantRoleRequest("HR", []);

        var (role, error) = await _sut.CreateAsync(_tenantId, null, request);

        Assert.Null(role);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<TenantRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NullCapabilities_TreatedAsEmpty_DoesNotThrow()
    {
        _repo.GetByNameAsync(_tenantId, "Empty", Arg.Any<CancellationToken>()).Returns((TenantRole?)null);

        var request = new CreateTenantRoleRequest("Empty", null!);

        var (role, error) = await _sut.CreateAsync(_tenantId, null, request);

        Assert.Null(error);
        Assert.Empty(role!.Capabilities);
    }

    // ── Update ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ExistingTemplate_UpdatesFields()
    {
        var role = TenantRole.Create(_tenantId, "Old Name", [TenantRoleCapabilities.UsersManage], null);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _repo.GetByNameAsync(_tenantId, "New Name", Arg.Any<CancellationToken>()).Returns((TenantRole?)null);

        var request = new UpdateTenantRoleRequest("New Name", [TenantRoleCapabilities.SchedulesManage]);

        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(error);
        Assert.Equal("New Name", dto!.Name);
        Assert.Equal(new[] { TenantRoleCapabilities.SchedulesManage }, role.Capabilities.ToArray());
        await _repo.Received(1).UpdateAsync(role, Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UnchangedName_DoesNotCheckUniqueness()
    {
        var role = TenantRole.Create(_tenantId, "HR", [], null);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var request = new UpdateTenantRoleRequest("HR", [TenantRoleCapabilities.UsersManage]);

        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _repo.DidNotReceive().GetByNameAsync(_tenantId, "HR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_TemplateNotFound_ReturnsError()
    {
        _repo.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((TenantRole?)null);

        var request = new UpdateTenantRoleRequest("X", []);
        var (dto, error) = await _sut.UpdateAsync(_tenantId, Guid.NewGuid(), request);

        Assert.Null(dto);
        Assert.Equal("Template not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_RenameToExistingActiveName_ReturnsError_DoesNotSave()
    {
        var role = TenantRole.Create(_tenantId, "HR", [], null);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _repo.GetByNameAsync(_tenantId, "Закупка", Arg.Any<CancellationToken>())
             .Returns(TenantRole.Create(_tenantId, "Закупка", [], null));

        var request = new UpdateTenantRoleRequest("Закупка", []);
        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(dto);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UnknownCapability_ReturnsValidationError_DoesNotSave()
    {
        var role = TenantRole.Create(_tenantId, "HR", [], null);
        _repo.GetByIdAsync(_tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var request = new UpdateTenantRoleRequest("HR", ["bogus.key"]);
        var (dto, error) = await _sut.UpdateAsync(_tenantId, role.Id, request);

        Assert.Null(dto);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Deactivate ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_Existing_ArchivesAndSaves()
    {
        _repo.DeactivateAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var (success, error) = await _sut.DeactivateAsync(_tenantId, Guid.NewGuid());

        Assert.True(success);
        Assert.Null(error);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_NotFoundOrAlreadyInactive_ReturnsError_DoesNotSave()
    {
        _repo.DeactivateAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var (success, error) = await _sut.DeactivateAsync(_tenantId, Guid.NewGuid());

        Assert.False(success);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Capability catalog ───────────────────────────────────────────────

    [Fact]
    public void GetCapabilityCatalog_ReturnsEveryCapabilityInAll_GroupedBySpecialty()
    {
        var groups = _sut.GetCapabilityCatalog();

        var flatKeys = groups.SelectMany(g => g.Capabilities).Select(c => c.Key).ToList();

        Assert.Equal(TenantRoleCapabilities.All.OrderBy(k => k, StringComparer.Ordinal),
            flatKeys.Distinct().OrderBy(k => k, StringComparer.Ordinal));
        Assert.Contains(groups, g => g.Capabilities.Any(c => c.Key == TenantUserPermissions.LegalEntitiesManage));
    }
}
