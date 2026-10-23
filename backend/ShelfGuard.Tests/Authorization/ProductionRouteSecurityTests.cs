using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class ProductionRouteSecurityTests
{
    [Theory]
    [InlineData(true, true, 0, HttpStatusCode.OK)]
    [InlineData(false, true, 0, HttpStatusCode.Forbidden)]
    [InlineData(true, false, 0, HttpStatusCode.Forbidden)]
    [InlineData(true, true, 600, HttpStatusCode.Forbidden)]
    public async Task Request_sync_requires_live_permission_assigned_production_location_and_recent_MFA(
        bool permitted, bool assigned, int mfaAge, HttpStatusCode expected)
    {
        var tenant = Tenant.Create("Synthetic route tenant", Guid.NewGuid().ToString("N")); tenant.UpdateModules(["production"]);
        var id = Guid.NewGuid(); var location = Guid.NewGuid(); var user = Guid.NewGuid();
        var detail = new ProductionRequestDetailDto(id, Guid.NewGuid(), "Store", location, "Production", DateOnly.FromDateTime(DateTime.UtcNow),
            "accepted", "manual", null, null, DateTime.UtcNow, null, null, null, null, null, []);
        var svc = Substitute.For<IProductionRequestService>(); svc.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(detail);
        svc.SyncOrdersAsync(id, user, Arg.Any<CancellationToken>()).Returns((detail, null, null));
        var access = Substitute.For<IWorkAccessContext>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(new WorkAccessScope(tenant.Id, user, "store_manager", false,
            assigned ? [location] : [], new HashSet<string>(), new Dictionary<string, bool> { ["production.manage"] = permitted }));
        var tenants = Substitute.For<ITenantRepository>(); tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var validator = Substitute.For<IAccessSessionValidator>(); validator.IsValidAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Secret"] = "SYNTHETIC-PRODUCTION-ROUTE-KEY-000000000000000000000000", ["Jwt:Issuer"] = "route", ["Jwt:Audience"] = "route" }).Build();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(ProductionRequestsController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyRequests()));
        builder.Services.AddSingleton(svc); builder.Services.AddSingleton(Substitute.For<IProductionDemandService>());
        builder.Services.AddSingleton(access); builder.Services.AddSingleton(tenants); builder.Services.AddSingleton(validator);
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddScoped<IAuthorizationHandler, WorkPermissionHandler>(); builder.Services.AddApiSecurity(config);
        await using var app = builder.Build(); app.UseRouting(); app.UseApiSecurity(); app.MapControllers(); await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var jwt = new JwtService(config);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(user, "synthetic@example.test", "store_manager", tenant.Id, null,
            sessionId: Guid.NewGuid(), mfaVerifiedAt: DateTime.UtcNow.AddSeconds(-mfaAge)));
        Assert.Equal(expected, (await client.PostAsync($"/api/production-requests/{id}/sync-orders", null)).StatusCode);
        if (expected != HttpStatusCode.OK) await svc.DidNotReceive().SyncOrdersAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
    private sealed class OnlyRequests : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(ProductionRequestsController)) feature.Controllers.Remove(c); }
    }
}
