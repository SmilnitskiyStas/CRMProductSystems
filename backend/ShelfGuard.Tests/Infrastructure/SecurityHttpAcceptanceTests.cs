using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using OtpNet;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

[Collection("Session database")]
public sealed class SecurityHttpAcceptanceTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB") ?? "";
    private const string Key = "SYNTHETIC-HTTP-ACCEPTANCE-KEY-000000000000000000000000";
    private const string Password = "Synthetic-Http-Password42";
    private string runtimeConnection = "";
    private readonly TestClock clock = new();
    private AppDbContext Admin() => TestPostgres.NewContext(connection);

    public async Task InitializeAsync()
    {
        await using var db = Admin();
        // Exercise the actual migration chain, not EnsureCreated (which omits RLS).
        await db.Database.MigrateAsync();
        var role = "http_acceptance_" + Guid.NewGuid().ToString("N");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        // Both values consist solely of a fixed identifier prefix plus generated hex.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;");
        await db.Database.ExecuteSqlRawAsync($"GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {role}; GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {role};");
#pragma warning restore EF1002
        runtimeConnection = new NpgsqlConnectionStringBuilder(connection) { Username = role, Password = password }.ConnectionString;
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<WebApplication> Server()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.Configuration.Sources.Clear(); // Never load appsettings.Secrets, environment credentials, or providers.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = runtimeConnection, ["Jwt:Secret"] = Key,
            ["Jwt:Issuer"] = "http-acceptance", ["Jwt:Audience"] = "http-acceptance",
            ["Frontend:BaseUrl"] = "https://synthetic.example" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new TestControllers()));
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        builder.Services.AddApiSecurity(builder.Configuration);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler(); app.UseRouting(); app.UseApiSecurity(); app.MapControllers();
        await app.StartAsync(); return app;
    }
    private static HttpClient Client(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        // Cookies are passed explicitly: tests check the actual Secure/SameSite/HttpOnly response.
        return new(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(address) };
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    private static string Cookie(HttpResponseMessage response)
    {
        var value = Assert.Single(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith("refreshToken="));
        Assert.Contains("httponly", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", value, StringComparison.OrdinalIgnoreCase);
        return value.Split(';')[0];
    }
    private static string Token(JsonElement body) => body.GetProperty("accessToken").GetString()!;
    private static void Bearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    private static string Code(string secret) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
    private async Task<(Tenant Tenant, User User, Location Location, Location Hidden)> Seed(string role = "store_manager", bool mfa = false)
    {
        var tenant = Tenant.Create("Synthetic HTTP tenant", Guid.NewGuid().ToString("N")); tenant.UpdateModules(["inventory", "production"]);
        var location = new Location { TenantId = tenant.Id, Name = "Assigned" }; var hidden = new Location { TenantId = tenant.Id, Name = "Hidden" };
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", BCrypt.Net.BCrypt.HashPassword(Password, 4), role);
        if (mfa) { user.SetPendingTotpSecret(new TotpService().GenerateSecret()); user.EnableTotp([new JwtService(Config()).HashToken("ABCD2345")]); }
        await using var db = Admin(); db.AddRange(tenant, location, hidden, user, UserLocation.Create(tenant.Id, user.Id, location.Id, null)); await db.SaveChangesAsync();
        return (tenant, user, location, hidden);
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = Key, ["Jwt:Issuer"] = "http-acceptance", ["Jwt:Audience"] = "http-acceptance" }).Build();
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body) => await client.PostAsJsonAsync(path, body);
    private static async Task<JsonElement> Login(HttpClient client, User user) => await Json(await Post(client, "/api/auth/login", new { user.Email, password = Password }));

    [RecoveryPostgresFact]
    public async Task Real_migrations_enable_RLS_for_runtime_role_without_anonymous_tenant_data_access()
    {
        var (tenant, _, _, _) = await Seed();
        await using var runtime = TestPostgres.NewContext(runtimeConnection);
        await runtime.Database.OpenConnectionAsync();
        await using var command = runtime.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT (NOT rolsuper AND NOT rolbypassrls), row_security_active('recipes'::regclass) FROM pg_roles WHERE rolname = current_user";
        await using (var reader = await command.ExecuteReaderAsync()) { Assert.True(await reader.ReadAsync()); Assert.True(reader.GetBoolean(0)); Assert.True(reader.GetBoolean(1)); }
        Assert.Empty(await runtime.Locations.Where(l => l.TenantId == tenant.Id).ToListAsync());
        await using var admin = Admin(); Assert.Empty(await admin.Database.GetPendingMigrationsAsync());
        Assert.Contains("20261023165833_EnforceAccessMatrix", await admin.Database.GetAppliedMigrationsAsync());
    }

    [RecoveryPostgresFact]
    public async Task Password_enrollment_OTP_cookie_refresh_and_logout_flow_works_under_RLS()
    {
        var (_, user, _, _) = await Seed(); await using var app = await Server(); using var client = Client(app);
        var loginResponse = await Post(client, "/api/auth/login", new { user.Email, password = Password }); var login = await Json(loginResponse);
        Assert.True(login.GetProperty("requiresMfaEnrollment").GetBoolean()); Assert.False(loginResponse.Headers.Contains("Set-Cookie"));
        var proof = login.GetProperty("enrollmentToken").GetString()!;
        Bearer(client, proof); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode); client.DefaultRequestHeaders.Authorization = null;
        var setup = await Json(await Post(client, "/api/auth/mfa-enrollment/setup", new { enrollmentToken = proof }));
        var invalid = await Post(client, "/api/auth/mfa-enrollment/enable", new { enrollmentToken = proof, code = "invalid" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode); Assert.False(invalid.Headers.Contains("Set-Cookie"));
        var enabledResponse = await Post(client, "/api/auth/mfa-enrollment/enable", new { enrollmentToken = proof, code = Code(setup.GetProperty("secret").GetString()!) });
        var enabled = await Json(enabledResponse); var cookie = Cookie(enabledResponse); Assert.Equal(8, enabled.GetProperty("recoveryCodes").GetArrayLength());
        Assert.Contains("no-store", string.Join(',', enabledResponse.Headers.CacheControl?.ToString() ?? ""));
        var access = Token(enabled); Bearer(client, access); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/auth/2fa/disable", new { password = Password, code = enabled.GetProperty("recoveryCodes")[0].GetString() })).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", cookie); var refreshedResponse = await client.PostAsync("/api/auth/refresh", null); var refreshed = await Json(refreshedResponse); Cookie(refreshedResponse);
        Assert.Equal(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(access).Claims.Single(c => c.Type == "mfa_at").Value,
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(Token(refreshed)).Claims.Single(c => c.Type == "mfa_at").Value);
        Bearer(client, Token(refreshed)); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout-all", null)).StatusCode);
        Bearer(client, access); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Capability_only_staff_receives_its_live_grants_in_the_MFA_login_response()
    {
        var (tenant, user, _, _) = await Seed("staff", mfa: true);
        var template = TenantRole.Create(tenant.Id, "Synthetic production", ["production.view", "production.complete"], null, ["production"]);
        await using (var db = Admin()) { db.Add(template); var current = await db.Users.SingleAsync(u => u.Id == user.Id); current.SetTenantRole(template.Id);
            db.Add(UserPermissionGrant.Create(tenant.Id, user.Id, "ai_assistant.use", DateTime.UtcNow.AddMinutes(10), user.Id)); await db.SaveChangesAsync(); }
        await using var app = await Server(); using var client = Client(app); var challenge = await Login(client, user);
        var response = await Json(await Post(client, "/api/auth/2fa/verify", new { challengeToken = challenge.GetProperty("challengeToken").GetString(), code = Code(user.TotpSecret!) }));
        var profile = response.GetProperty("user"); Assert.Contains("production.complete", profile.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("production", profile.GetProperty("tabs").EnumerateArray().Select(x => x.GetString())); Assert.True(profile.GetProperty("permissions").GetProperty("ai_assistant.use").GetBoolean());
        Bearer(client, Token(response)); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/production/orders")).StatusCode);
    }

    private sealed class TestControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(AuthController) && controller.AsType() != typeof(ProductionController)) feature.Controllers.Remove(controller);
        }
    }
    private sealed class TestClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow; }
}
