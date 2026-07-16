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
/// TASK-363 (Block 12 pre-launch audit): a provider_admin passes the ProviderCanInvite policy
/// (same as the real owner), but must never be able to mint, promote to, or deactivate the
/// literal "provider" (owner) role — that would unlock ProviderController's ProviderOnly-gated
/// endpoints (tenant CRUD, impersonation, platform logs), which are deliberately restricted to
/// the single owner account per v1-spec.md §3.2.
/// </summary>
public sealed class ProviderTeamServiceTests
{
    private readonly IUserRepository        _users         = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher        _hasher        = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ProviderTeamService _sut;

    public ProviderTeamServiceTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
        _sut = new ProviderTeamService(_users, _hasher, _refreshTokens);
    }

    // ── InviteMemberAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Invite_ProviderAdminRequestsOwnerRole_IsRejected()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var req = new InviteProviderMemberRequest(
            Email: "new-owner@example.com", FullName: "New Owner", Role: AppRoles.Provider);

        var (member, error) = await _sut.InviteMemberAsync(req, AppRoles.ProviderAdmin, default);

        Assert.Null(member);
        Assert.NotNull(error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invite_OwnerRequestsOwnerRole_Succeeds()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var req = new InviteProviderMemberRequest(
            Email: "co-owner@example.com", FullName: "Co Owner", Role: AppRoles.Provider,
            Password: "SecurePass123");

        var (member, error) = await _sut.InviteMemberAsync(req, AppRoles.Provider, default);

        Assert.Null(error);
        Assert.NotNull(member);
        Assert.Equal(AppRoles.Provider, member!.Role);
        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invite_ProviderAdminRequestsAdminRole_Succeeds()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var req = new InviteProviderMemberRequest(
            Email: "agent@example.com", FullName: "Agent", Role: AppRoles.ProviderAgent,
            Password: "SecurePass123");

        var (member, error) = await _sut.InviteMemberAsync(req, AppRoles.ProviderAdmin, default);

        Assert.Null(error);
        Assert.NotNull(member);
    }

    // ── UpdateMemberAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Update_ProviderAdminPromotesSelfToOwner_IsRejected()
    {
        var actingAdmin = User.Create(null, "admin@example.com", "Admin", "hash", AppRoles.ProviderAdmin);
        _users.GetByIdAsync(actingAdmin.Id, Arg.Any<CancellationToken>()).Returns(actingAdmin);

        var req = new UpdateProviderMemberRequest(FullName: "Admin", Role: AppRoles.Provider);

        var (member, error) = await _sut.UpdateMemberAsync(actingAdmin.Id, req, AppRoles.ProviderAdmin, default);

        Assert.Null(member);
        Assert.NotNull(error);
        Assert.Equal(AppRoles.ProviderAdmin, actingAdmin.Role); // role unchanged
        _users.DidNotReceive().Update(Arg.Any<User>());
    }

    [Fact]
    public async Task Update_ProviderAdminPromotesTeammateToOwner_IsRejected()
    {
        var teammate = User.Create(null, "agent@example.com", "Agent", "hash", AppRoles.ProviderAgent);
        _users.GetByIdAsync(teammate.Id, Arg.Any<CancellationToken>()).Returns(teammate);

        var req = new UpdateProviderMemberRequest(FullName: "Agent", Role: AppRoles.Provider);

        var (member, error) = await _sut.UpdateMemberAsync(teammate.Id, req, AppRoles.ProviderAdmin, default);

        Assert.Null(member);
        Assert.NotNull(error);
        Assert.Equal(AppRoles.ProviderAgent, teammate.Role);
    }

    [Fact]
    public async Task Update_OwnerPromotesTeammateToOwner_Succeeds()
    {
        var teammate = User.Create(null, "admin@example.com", "Admin", "hash", AppRoles.ProviderAdmin);
        _users.GetByIdAsync(teammate.Id, Arg.Any<CancellationToken>()).Returns(teammate);

        var req = new UpdateProviderMemberRequest(FullName: "Admin", Role: AppRoles.Provider);

        var (member, error) = await _sut.UpdateMemberAsync(teammate.Id, req, AppRoles.Provider, default);

        Assert.Null(error);
        Assert.NotNull(member);
        Assert.Equal(AppRoles.Provider, teammate.Role);
    }

    [Fact]
    public async Task Update_ProviderAdminDemotesOwner_IsRejected()
    {
        // Pre-existing guard, still verified: demoting the literal owner stays blocked.
        var owner = User.Create(null, "owner@example.com", "Owner", "hash", AppRoles.Provider);
        _users.GetByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var req = new UpdateProviderMemberRequest(FullName: "Owner", Role: AppRoles.ProviderAdmin);

        var (member, error) = await _sut.UpdateMemberAsync(owner.Id, req, AppRoles.ProviderAdmin, default);

        Assert.Null(member);
        Assert.NotNull(error);
        Assert.Equal(AppRoles.Provider, owner.Role);
    }

    // ── DeactivateMemberAsync ───────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_ProviderAdminDeactivatesOwner_IsRejected()
    {
        var owner = User.Create(null, "owner@example.com", "Owner", "hash", AppRoles.Provider);
        _users.GetByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var (success, error) = await _sut.DeactivateMemberAsync(owner.Id, AppRoles.ProviderAdmin, default);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.True(owner.IsActive);
    }

    [Fact]
    public async Task Deactivate_OwnerDeactivatesAnotherOwner_Succeeds()
    {
        var owner = User.Create(null, "owner2@example.com", "Owner Two", "hash", AppRoles.Provider);
        _users.GetByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var (success, error) = await _sut.DeactivateMemberAsync(owner.Id, AppRoles.Provider, default);

        Assert.True(success);
        Assert.Null(error);
        Assert.False(owner.IsActive);
    }

    [Fact]
    public async Task Deactivate_ProviderAdminDeactivatesAgent_Succeeds()
    {
        var agent = User.Create(null, "agent@example.com", "Agent", "hash", AppRoles.ProviderAgent);
        _users.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);

        var (success, error) = await _sut.DeactivateMemberAsync(agent.Id, AppRoles.ProviderAdmin, default);

        Assert.True(success);
        Assert.Null(error);
        Assert.False(agent.IsActive);
    }
}
