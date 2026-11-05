using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class ConsumerPurchasesControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Purchases_reject_staff_or_invalid_consumer_identity(string? consumerClaim)
    {
        var service = Substitute.For<ILoyaltyService>();
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()), new("role", "enterprise_admin") };
        if (consumerClaim is not null) claims.Add(new Claim("consumer_account_id", consumerClaim));
        var controller = new ConsumerLoyaltyController(service) { ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        } };
        Assert.IsType<ForbidResult>(await controller.GetPurchases(Guid.NewGuid()));
        Assert.IsType<ForbidResult>(await controller.GetReceipt(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Empty(service.ReceivedCalls());
    }

    [Fact]
    public async Task Purchases_use_only_authenticated_consumer_and_requested_store()
    {
        var service = Substitute.For<ILoyaltyService>();
        var consumer = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var store = Guid.NewGuid();
        var result = new PagedResult<ConsumerPurchaseDto> { Items = [], TotalCount = 0, Page = 2, PageSize = 10 };
        service.GetPurchasesAsync(consumer, tenant, store, 2, 10, default)
            .Returns((result, null, null));
        var controller = new ConsumerLoyaltyController(service) { ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("consumer_account_id", consumer.ToString()) }, "test")) },
        } };
        var response = Assert.IsType<OkObjectResult>(await controller.GetPurchases(tenant, store, 2, 10));
        Assert.Same(result, response.Value);
    }
}
