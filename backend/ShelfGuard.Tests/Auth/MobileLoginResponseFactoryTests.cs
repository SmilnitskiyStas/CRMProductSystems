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
        Assert.Empty(response.Access.Capabilities);
        Assert.Empty(response.Access.Tabs);
        Assert.Null(response.User.TenantId);
        Assert.Null(response.User.StoreId);
        Assert.Equal("token", response.PersonalAccessToken);
        Assert.Null(response.WorkspaceAccessToken);
    }

}
