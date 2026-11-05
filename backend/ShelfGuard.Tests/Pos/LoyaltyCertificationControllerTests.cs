using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Services;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class LoyaltyCertificationControllerTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Only_successfully_resolved_operator_membership_receives_a_scoped_quote(bool identifier)
    {
        var tenant = Guid.NewGuid(); var member = Guid.NewGuid(); var register = Guid.NewGuid(); var actor = Guid.NewGuid();
        var service = Substitute.For<ILoyaltyService>(); var context = Substitute.For<ITenantContext>();
        context.TenantId.Returns(tenant);
        var resolved = new ResolveLoyaltyCodeResult(member, null, "Synthetic member", null, 100);
        service.ResolveCodeAsync(tenant, actor, "code", Arg.Any<CancellationToken>()).Returns((resolved, null, null));
        service.ResolveNumericIdentifierAsync(tenant, "card", "123", Arg.Any<CancellationToken>()).Returns((resolved, null, null));
        var quotes = Substitute.For<IPosLoyaltyQuoteIssuer>();
        quotes.ForResolvedAsync(tenant, register, member, Arg.Any<CancellationToken>()).Returns(new PosLoyaltyGrant(
            "lo1.synthetic", DateTime.UtcNow.AddDays(14), Guid.NewGuid(), "Frozen", 25, true, null));
        var controller = new LoyaltyController(service, context, quotes) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString()), new("register_id", register.ToString())], "synthetic")) } } };
        var result = identifier ? await controller.ResolveIdentifier(new("card", "123"), default) : await controller.ResolveCode(new("code"), default);
        var dto = Assert.IsType<ResolveLoyaltyCodeResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("lo1.synthetic", dto.LoyaltySnapshot); Assert.Equal(25, dto.TierDiscountPercent);
        await quotes.Received(1).ForResolvedAsync(tenant, register, member, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Failed_identification_never_issues_terms(bool identifier)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var service = Substitute.For<ILoyaltyService>();
        var context = Substitute.For<ITenantContext>(); context.TenantId.Returns(tenant);
        service.ResolveCodeAsync(tenant, actor, "bad", Arg.Any<CancellationToken>()).Returns(((ResolveLoyaltyCodeResult?)null, "denied", 409));
        service.ResolveNumericIdentifierAsync(tenant, "card", "bad", Arg.Any<CancellationToken>()).Returns(((ResolveLoyaltyCodeResult?)null, "denied", 404));
        var quotes = Substitute.For<IPosLoyaltyQuoteIssuer>();
        var controller = new LoyaltyController(service, context, quotes) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString()), new("register_id", Guid.NewGuid().ToString())], "synthetic")) } } };
        var result = identifier ? await controller.ResolveIdentifier(new("card", "bad"), default) : await controller.ResolveCode(new("bad"), default);
        Assert.NotEqual(200, Assert.IsType<ObjectResult>(result).StatusCode);
        await quotes.DidNotReceiveWithAnyArgs().ForResolvedAsync(default, default, default, default);
    }
}
