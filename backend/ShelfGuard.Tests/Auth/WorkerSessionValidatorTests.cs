using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class WorkerSessionValidatorTests
{
    [Theory]
    [InlineData("valid",true)]
    [InlineData("version",false)]
    [InlineData("tenant",false)]
    [InlineData("identity",false)]
    [InlineData("role",false)]
    [InlineData("purpose",false)]
    [InlineData("staff-session",false)]
    [InlineData("disabled",false)]
    [InlineData("inactive",false)]
    [InlineData("locked",false)]
    [InlineData("different-email",false)]
    public async Task Machine_access_is_bound_to_live_configured_identity_tenant_role_and_security_version(string scenario,bool expected)
    {
        var services=new ServiceCollection(); var databaseName=Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o=>o.UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
        await using var provider=services.BuildServiceProvider();
        var tenant=Tenant.Create("Synthetic worker tenant",Guid.NewGuid().ToString("N"));
        var user=User.Create(tenant.Id,"worker@synthetic.example","Synthetic","hash","network_manager");
        if(scenario=="inactive")user.Deactivate();
        if(scenario=="locked")user.RegisterFailedLogin(1,TimeSpan.FromMinutes(15));
        await using(var scope=provider.CreateAsyncScope()) { var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.AddRange(tenant,user);await db.SaveChangesAsync(); }
        var claims=new List<Claim> { new(ClaimTypes.NameIdentifier,(scenario=="identity"?Guid.NewGuid():user.Id).ToString()),
            new(ClaimTypes.Role,scenario=="role"?"store_manager":user.Role),new("tenant_id",(scenario=="tenant"?Guid.NewGuid():tenant.Id).ToString()),
            new("auth_ver",(scenario=="version"?user.AuthVersion+1:user.AuthVersion).ToString()),new("purpose",scenario=="purpose"?"staff":"worker-job") };
        if(scenario=="staff-session")claims.Add(new("sid",Guid.NewGuid().ToString()));
        var email=scenario=="disabled"?"":scenario=="different-email"?"other@synthetic.example":user.Email;
        var validator=new WorkerSessionValidator(provider.GetRequiredService<IServiceScopeFactory>(),new(email));
        Assert.Equal(expected,await validator.IsValidAsync(new ClaimsPrincipal(new ClaimsIdentity(claims,"synthetic"))));
    }
}
