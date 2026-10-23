using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class SecurityPipelineTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Jwt:Secret"] = "SYNTHETIC-HTTP-PIPELINE-KEY-000000000000000000000000",
        ["Jwt:Issuer"] = "pipeline", ["Jwt:Audience"] = "pipeline" }).Build();
    private static async Task<WebApplication> Server()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var validator = Substitute.For<IAccessSessionValidator>();
        validator.IsValidAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        builder.Services.AddSingleton(validator); builder.Services.AddApiSecurity(Config);
        var app = builder.Build(); app.UseRouting(); app.UseApiSecurity();
        app.MapPost("/critical-probe", () => "synthetic").RequireAuthorization().RequireRateLimiting("register-pin-manage");
        app.MapGet("/probe", () => "synthetic").RequireAuthorization();
        app.MapGet(ApiSecurityRegistration.ConsumerSupportHubPath, () => "synthetic").RequireAuthorization();
        await app.StartAsync(); return app;
    }
    private static HttpClient Client(WebApplication app, string? token = null)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    [Fact]
    public async Task Shared_pipeline_authenticates_before_subject_partitioned_limits()
    {
        await using var app = await Server(); var jwt = new JwtService(Config);
        using var first = Client(app, jwt.GenerateAccessToken(Guid.NewGuid(), "first@synthetic.example", "store_manager", Guid.NewGuid(), null, sessionId: Guid.NewGuid()));
        using var second = Client(app, jwt.GenerateAccessToken(Guid.NewGuid(), "second@synthetic.example", "store_manager", Guid.NewGuid(), null, sessionId: Guid.NewGuid()));
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/critical-probe", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsync("/critical-probe", null)).StatusCode);
        // Same remote IP, different signed subject: this fails if limits run before authentication.
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync("/critical-probe", null)).StatusCode);
    }
    [Fact]
    public async Task Shared_bearer_registration_rejects_enrollment_audience_and_limits_query_tokens_to_the_hub()
    {
        await using var app = await Server(); var jwt = new JwtService(Config); var id = Guid.NewGuid();
        using var proof = Client(app, jwt.GenerateMfaEnrollmentToken(id, 0));
        Assert.Equal(HttpStatusCode.Unauthorized, (await proof.GetAsync("/probe")).StatusCode);
        using var anonymous = Client(app);
        var token = Uri.EscapeDataString(jwt.GenerateAccessToken(id, "synthetic@example.test", "store_manager", Guid.NewGuid(), null, sessionId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/probe?access_token=" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(ApiSecurityRegistration.ConsumerSupportHubPath + "?access_token=" + token)).StatusCode);
    }
}
