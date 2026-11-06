using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.BeautyControl;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Microsoft.AspNetCore.Authorization;

namespace ShelfGuard.Tests.BeautyControl;

public sealed class BeautyTenantLinkRepositoryTests
{
    private sealed class ThrowingInterceptor(Exception ex) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) => throw ex;
    }

    private static BeautyTenantLinkRepository Repo(Exception toThrow) =>
        new(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"links-{Guid.NewGuid()}").AddInterceptors(new ThrowingInterceptor(toThrow)).Options),
            NullLogger<BeautyTenantLinkRepository>.Instance);

    private static DbUpdateException Pg(string sqlState, string? constraint) =>
        new("synthetic", new PostgresException("synthetic", "ERROR", "ERROR", sqlState, constraintName: constraint));

    private static BeautyTenantLink NewLink() =>
        BeautyTenantLink.Create(Guid.NewGuid(), null, "salon-x", "active", DateTime.UtcNow);

    [Fact]
    public async Task Unique_Violation_On_TenantId_Index_Returns_False()
    {
        var repo = Repo(Pg(PostgresErrorCodes.UniqueViolation, "IX_beauty_tenant_links_TenantId"));
        Assert.False(await repo.TryAddAsync(NewLink(), CancellationToken.None));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "IX_beauty_tenant_links_BeautySlug")]
    [InlineData(PostgresErrorCodes.UniqueViolation, null)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "IX_beauty_tenant_links_TenantId")]
    [InlineData(PostgresErrorCodes.NotNullViolation, null)]
    public async Task Other_Db_Errors_Are_Not_Swallowed(string sqlState, string? constraint)
    {
        var repo = Repo(Pg(sqlState, constraint));
        await Assert.ThrowsAsync<DbUpdateException>(() => repo.TryAddAsync(NewLink(), CancellationToken.None));
    }

    [Fact]
    public async Task DbUpdateException_Without_Postgres_Inner_Is_Not_Swallowed()
    {
        var repo = Repo(new DbUpdateException("synthetic"));
        await Assert.ThrowsAsync<DbUpdateException>(() => repo.TryAddAsync(NewLink(), CancellationToken.None));
    }
}

public sealed class BeautyRemoteValidationTests
{
    private readonly IBeautyControlClient _client = Substitute.For<IBeautyControlClient>();
    private readonly IBeautyTenantLinkRepository _links = Substitute.For<IBeautyTenantLinkRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IActivityLogRepository _logs = Substitute.For<IActivityLogRepository>();
    private readonly BeautyProvisioningService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly BeautyActor Actor = new("prov-1", "Provider");

    public BeautyRemoteValidationTests()
    {
        var t = Tenant.Create("Acme", "acme");
        Assert.Null(t.UpdateBusinessType("beauty"));
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(t);
        _links.TryAddAsync(Arg.Any<BeautyTenantLink>(), Arg.Any<CancellationToken>()).Returns(true);
        _sut = new(_client, _links, _tenants, _logs, TimeProvider.System, NullLogger<BeautyProvisioningService>.Instance);
    }

    [Theory]
    [InlineData("Bad Slug!", "active")]
    [InlineData("salon-x", "weird")]
    [InlineData("salon-x", "")]
    [InlineData("a-very-long-slug-0123456789-0123456789-0123456789-0123456789-0123456789", "active")]
    public async Task Create_Invalid_Remote_Slug_Or_Status_Is_Not_Persisted(string slug, string status)
    {
        _client.CreateTenantAsync(Arg.Any<BeautyCreateTenantCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BeautyResult<BeautyCreatedTenant>(new BeautyCreatedTenant(
                Guid.NewGuid(), _tenantId.ToString(), slug, status, ["beauty_booking"], null,
                new BeautyInviteInfo("https://app.example/invite#t=X", null)), HttpStatus: 201));

        var result = await _sut.CreateAsync(
            new(_tenantId, "Salon X", "salon-x", "o@x.test", null, null, null), Actor, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("beauty_bad_response", result.Code);
        await _links.DidNotReceive().TryAddAsync(Arg.Any<BeautyTenantLink>(), Arg.Any<CancellationToken>());
        await _links.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Bad Slug!", "active")]
    [InlineData("salon-x", "weird")]
    public async Task Get_Invalid_Remote_Slug_Or_Status_Does_Not_Touch_Link(string slug, string status)
    {
        var link = BeautyTenantLink.Create(_tenantId, null, "salon-x", "active", DateTime.UtcNow);
        _links.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(link);
        _client.GetTenantAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BeautyResult<BeautyTenantInfo>(new BeautyTenantInfo(
                Guid.NewGuid(), null, slug, "Salon", status, [], null, null, null)));

        var result = await _sut.GetAsync(_tenantId, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("salon-x", link.BeautySlug);
        Assert.Equal("active", link.BeautyStatus);
        await _links.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class ProviderBeautyControllerGuardTests
{
    [Fact]
    public void Controller_Is_ProviderOnly()
    {
        var auth = Assert.Single(typeof(ProviderBeautyController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AppPolicies.ProviderOnly, auth.Policy);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Suspend")]
    [InlineData("Resume")]
    [InlineData("OwnerInvite")]
    [InlineData("SetModules")]
    public void Mutating_Actions_Require_Recent_Mfa(string action) =>
        Assert.NotEmpty(typeof(ProviderBeautyController).GetMethod(action)!.GetCustomAttributes(typeof(RequireRecentMfaIfEnrolledAttribute), true));

    [Fact]
    public void Get_Does_Not_Require_Mfa() =>
        Assert.Empty(typeof(ProviderBeautyController).GetMethod("Get")!.GetCustomAttributes(typeof(RequireRecentMfaIfEnrolledAttribute), true));
}
