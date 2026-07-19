using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Users;

/// <summary>i18n rollout Block 1 (TASK-375) — PreferredLocale validation on self-service profile update.</summary>
public sealed class UserServicePreferredLocaleTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ILegalEntityService _legalEntities = Substitute.For<ILegalEntityService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly ILocationService _locations = Substitute.For<ILocationService>();
    private readonly IUserLocationRepository _userLocations = Substitute.For<IUserLocationRepository>();
    private readonly UserService _sut;

    public UserServicePreferredLocaleTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("en")]
    public async Task UpdateMyProfile_accepts_supported_locale(string locale)
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var (dto, error) = await _sut.UpdateMyProfileAsync(user.Id,
            new UpdateMyProfileRequest("Test User", null, locale), default);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(locale, dto!.PreferredLocale);
        Assert.Equal(locale, user.PreferredLocale);
    }

    [Fact]
    public async Task UpdateMyProfile_rejects_unsupported_locale()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var (dto, error) = await _sut.UpdateMyProfileAsync(user.Id,
            new UpdateMyProfileRequest("Test User", null, "fr"), default);

        Assert.Null(dto);
        Assert.Equal("Unsupported locale 'fr'. Supported: uk, en.", error);
        // Rejected before any load/save — profile must stay untouched.
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMyProfile_null_locale_leaves_existing_value_unchanged()
    {
        var user = MakeUser();
        user.SetPreferredLocale("en");
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var (dto, error) = await _sut.UpdateMyProfileAsync(user.Id,
            new UpdateMyProfileRequest("Test User", null, null), default);

        Assert.Null(error);
        Assert.Equal("en", dto!.PreferredLocale);
    }

    private static User MakeUser() =>
        User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");
}
