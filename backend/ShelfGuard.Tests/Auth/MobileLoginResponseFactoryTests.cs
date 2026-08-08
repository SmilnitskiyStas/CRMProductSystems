using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Features.MobileAuth.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class MobileLoginResponseFactoryTests
{
    [Fact]
    public void Staff_response_exposes_workspace_access_and_effective_permissions()
    {
        var user = new AuthUserDto(
            Guid.NewGuid(), "staff@example.com", "Staff User", "store_manager",
            Guid.NewGuid(), "Store", Guid.NewGuid(),
            new Dictionary<string, bool> { ["stock"] = true },
            Capabilities: new[] { "stock.read" }, Tabs: new[] { "stock" });

        var response = MobileLoginResponseFactory.ForStaff("token", user);

        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Equal("store_manager", response.Access.Role);
        Assert.True(response.Access.Permissions["stock"]);
        Assert.Equal(user.TenantId, response.User.TenantId);
        Assert.Null(response.PersonalAccessToken);
        Assert.Equal("token", response.WorkspaceAccessToken);
    }

    [Fact]
    public void Consumer_response_stays_in_personal_mode()
    {
        var response = MobileLoginResponseFactory.ForConsumer(
            "token", Guid.NewGuid(), "Consumer User", "+380501234567");

        Assert.False(response.Access.CanAccessWorkspace);
        Assert.Equal("consumer", response.Access.Role);
        Assert.Empty(response.Access.Permissions);
        Assert.Null(response.User.TenantId);
        Assert.Equal("token", response.PersonalAccessToken);
        Assert.Null(response.WorkspaceAccessToken);
    }

    [Fact]
    public void Linked_staff_response_carries_both_tokens_with_staff_effective_access()
    {
        var user = new AuthUserDto(
            Guid.NewGuid(), "staff@example.com", "Staff User", "cashier",
            Guid.NewGuid(), "Store", Guid.NewGuid(),
            new Dictionary<string, bool> { ["pos"] = true },
            Capabilities: new[] { "pos.read" }, Tabs: new[] { "pos" });

        var response = MobileLoginResponseFactory.ForLinkedStaff(
            "personal-token", "workspace-token", user);

        Assert.Equal("personal-token", response.PersonalAccessToken);
        Assert.Equal("workspace-token", response.WorkspaceAccessToken);
        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Equal("cashier", response.Access.Role);
        Assert.True(response.Access.Permissions["pos"]);
        Assert.Equal(user.TenantId, response.User.TenantId);
    }
}
