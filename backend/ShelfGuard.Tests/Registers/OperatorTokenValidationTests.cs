using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Registers;

public sealed class OperatorTokenValidationTests
{
    private const string RegisterAudience = "shelfguard" + JwtAudiences.RegisterSuffix;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly Guid Register = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static TokenValidatedContext Context(IOperatorSessionValidator validator, bool endpointAllowsOperator, bool operatorToken = true)
    {
        var services = new ServiceCollection().AddSingleton(validator).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            endpointAllowsOperator
                ? new EndpointMetadataCollection(new AllowOperatorTokenAttribute())
                : new EndpointMetadataCollection(),
            "test"));

        var claims = new List<Claim> { new("tenant_id", Tenant.ToString()) };
        if (operatorToken)
        {
            claims.Add(new Claim("aud", RegisterAudience));
            claims.Add(new Claim("session_id", Session.ToString()));
            claims.Add(new Claim("register_id", Register.ToString()));
            claims.Add(new Claim("cashier_id", User.ToString()));
        }
        else
        {
            claims.Add(new Claim("aud", "shelfguard"));
        }

        return new TokenValidatedContext(
            http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };
    }

    private static IOperatorSessionValidator Validator(bool result)
    {
        var v = Substitute.For<IOperatorSessionValidator>();
        v.IsValidAsync(default, default, default, default, default).ReturnsForAnyArgs(result);
        return v;
    }

    [Fact]
    public async Task Operator_token_is_rejected_on_endpoints_that_do_not_opt_in()
    {
        var validator = Validator(true);
        var ctx = Context(validator, endpointAllowsOperator: false);

        await OperatorTokenValidation.ValidateAsync(ctx, RegisterAudience);

        Assert.NotNull(ctx.Result?.Failure);
        await validator.DidNotReceiveWithAnyArgs().IsValidAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Operator_token_is_accepted_on_opted_in_endpoints_while_the_session_is_valid()
    {
        var ctx = Context(Validator(true), endpointAllowsOperator: true);

        await OperatorTokenValidation.ValidateAsync(ctx, RegisterAudience);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task Operator_token_is_rejected_once_the_session_is_no_longer_valid()
    {
        var validator = Validator(false);
        var ctx = Context(validator, endpointAllowsOperator: true);

        await OperatorTokenValidation.ValidateAsync(ctx, RegisterAudience);

        Assert.NotNull(ctx.Result?.Failure);
        await validator.Received(1).IsValidAsync(Tenant, Session, Register, User, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regular_user_tokens_are_untouched_everywhere()
    {
        var validator = Validator(false);
        var ctx = Context(validator, endpointAllowsOperator: false, operatorToken: false);

        await OperatorTokenValidation.ValidateAsync(ctx, RegisterAudience);

        Assert.Null(ctx.Result);
        await validator.DidNotReceiveWithAnyArgs().IsValidAsync(default, default, default, default, default);
    }

    [Fact]
    public void Operator_tokens_carry_the_separate_register_audience_and_cashier_role()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = new string('k', 48), ["Jwt:Audience"] = "shelfguard", ["Jwt:Issuer"] = "shelfguard",
            }).Build();
        var jwt = new JwtService(config);

        var (token, _) = jwt.GenerateOperatorToken(User, "c@x.test", "C", Tenant, Guid.NewGuid(), Register, Session);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(RegisterAudience, parsed.Audiences);
        Assert.DoesNotContain("shelfguard", parsed.Audiences.Where(a => a == "shelfguard"));
        Assert.Equal("cashier", parsed.Claims.First(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.Equal(Session.ToString(), parsed.Claims.First(c => c.Type == "session_id").Value);

        var (staff, _) = (jwt.GenerateAccessToken(User, "s@x.test", "cashier", Tenant, null), 0);
        Assert.DoesNotContain(RegisterAudience, new JwtSecurityTokenHandler().ReadJwtToken(staff).Audiences);
    }
}
