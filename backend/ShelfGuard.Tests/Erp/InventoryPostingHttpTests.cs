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
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Services;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class InventoryPostingHttpTests(ErpSchemaFixture fixture)
{
    private const string Key = "SYNTHETIC-W4B-HTTP-SIGNING-KEY-000000000000000000000000";

    [ErpPostgresFact]
    public async Task Real_HTTP_command_posts_and_replays_exact_decimal_strings_despite_module_filter_tracking()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s); using var client = Client(app);
        await Token(client, s); var command = Body(s);
        var first = await Json(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", command), HttpStatusCode.OK);
        var replay = await Json(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", command), HttpStatusCode.OK);
        Assert.False(first.GetProperty("replayed").GetBoolean()); Assert.True(replay.GetProperty("replayed").GetBoolean());
        foreach (var key in new[] { "requestId", "documentId", "postingId", "entryId", "eventId", "quantityBefore", "quantityAfter", "postedAt" })
            Assert.Equal(first.GetProperty(key).GetRawText(), replay.GetProperty(key).GetRawText());
        Assert.Equal("3.25", first.GetProperty("quantityBefore").GetString()); Assert.Equal("5.00", first.GetProperty("quantityAfter").GetString());
        var status = await Json(await client.GetAsync($"/api/erp/inventory/scopes/{s.Location}"), HttpStatusCode.OK);
        Assert.True(status.GetProperty("canAdjust").GetBoolean());
        var changed = new Dictionary<string, object?>(command) { ["quantity"] = "6.00" };
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", changed), HttpStatusCode.Conflict, "inventory_posting_conflict");
    }

    [ErpPostgresTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task App_gate_and_database_fence_are_both_required_and_reported_separately(bool configured, bool fenced)
    {
        var s = await InventoryFenceScenario.Seed(fixture, fenced); await using var app = await Server(s, configured); using var client = Client(app);
        await Token(client, s);
        var status = await Json(await client.GetAsync($"/api/erp/inventory/scopes/{s.Location}"), HttpStatusCode.OK);
        Assert.Equal(configured, status.GetProperty("configured").GetBoolean()); Assert.Equal(fenced, status.GetProperty("fenced").GetBoolean());
        Assert.False(status.GetProperty("canAdjust").GetBoolean());
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s)), HttpStatusCode.Conflict,
            configured ? "inventory_posting_conflict" : "inventory_posting_disabled");
    }

    [ErpPostgresTheory]
    [InlineData("expectedQuantity")]
    [InlineData("quantity")]
    [InlineData("requestId")]
    [InlineData("locationId")]
    [InlineData("batchId")]
    [InlineData("reason")]
    public async Task Omitted_required_input_is_not_silently_treated_as_zero(string field)
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s); using var client = Client(app); await Token(client, s);
        var body = Body(s); body.Remove(field);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/erp/inventory/adjustments", body)).StatusCode);
    }

    [ErpPostgresFact]
    public async Task Invalid_precision_and_noop_are_400_and_foreign_batch_is_404()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var foreign = await InventoryFenceScenario.Seed(fixture);
        await using var app = await Server(s); using var client = Client(app); await Token(client, s);
        var body = Body(s); body["quantity"] = "1.001";
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", body), HttpStatusCode.BadRequest, "invalid_inventory_adjustment");
        body["quantity"] = "3.25";
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", body), HttpStatusCode.BadRequest, "invalid_inventory_adjustment");
        body["quantity"] = "5.00"; body["batchId"] = foreign.Batch;
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", body), HttpStatusCode.NotFound, "stock_batch_not_found");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/erp/inventory/scopes/{foreign.Location}")).StatusCode);
    }

    [ErpPostgresTheory]
    [InlineData(-1)]
    [InlineData(-301)]
    [InlineData(60)]
    public async Task Missing_expired_or_future_MFA_cannot_post(int offset)
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s); using var client = Client(app);
        await Token(client, s, mfa: offset == -1 ? null : DateTime.UtcNow.AddSeconds(offset));
        await Code(await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s)), HttpStatusCode.Forbidden, "mfa_step_up_required");
    }

    [ErpPostgresFact]
    public async Task Anonymous_staff_and_disabled_module_are_rejected()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s); using var client = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s))).StatusCode);
        await using (var db = fixture.Context())
        {
            var staff = User.Create(s.Tenant, Guid.NewGuid().ToString("N") + "@test.local", "Cashier", "hash", "cashier");
            db.Add(staff); db.Add(UserLocation.Create(s.Tenant, staff.Id, s.Location, null)); await db.SaveChangesAsync();
            await Token(client, s with { Actor = staff.Id });
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s))).StatusCode);
        await Token(client, s);
        await using (var db = fixture.Context())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenants SET \"Modules\"='[]'::jsonb WHERE \"Id\"={s.Tenant}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s))).StatusCode);
    }

    [ErpPostgresFact]
    public async Task Inactive_location_is_still_fenced_but_cannot_post()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s); using var client = Client(app); await Token(client, s);
        await using (var db = fixture.Context())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE locations SET \"IsActive\"=false WHERE \"Id\"={s.Location}");
        var status = await Json(await client.GetAsync($"/api/erp/inventory/scopes/{s.Location}"), HttpStatusCode.OK);
        Assert.True(status.GetProperty("fenced").GetBoolean()); Assert.False(status.GetProperty("canAdjust").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/erp/inventory/adjustments", Body(s))).StatusCode);
    }

    [ErpPostgresFact]
    public async Task Legacy_HTTP_stock_update_is_a_sanitized_conflict_even_when_application_gate_is_off()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var app = await Server(s, false); using var client = Client(app); await Token(client, s);
        var response = await client.PutAsJsonAsync($"/api/stock/{s.Batch}", new { quantity = 8m, batchNumber = "w4b-lot" });
        await Code(response, HttpStatusCode.Conflict, "inventory_scope_fenced");
        await using var db = fixture.Context(); Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(p => p.Id == s.Batch)).Quantity);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.TenantId == s.Tenant));
    }

    private async Task<WebApplication> Server(InventoryFenceScenario s, bool enabled = true, bool receiptsEnabled = true, Guid? extraLocation = null, bool transfersEnabled = true, bool writeOffsEnabled = true, bool posEnabled = false, bool posReturnsEnabled = false, bool productionEnabled = false)
    {
        var role = "w4b_http_" + Guid.NewGuid().ToString("N"); var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string connection;
        await using (var admin = fixture.Context())
        {
            await admin.Database.OpenConnectionAsync(); connection = admin.Database.GetConnectionString()!;
            var ddl = $"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS; GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO {role}; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO {role}";
            await admin.Database.ExecuteSqlRawAsync(ddl);
        }
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = new NpgsqlConnectionStringBuilder(connection) { Username = role, Password = password }.ConnectionString,
            ["Jwt:Secret"] = Key, ["Jwt:Issuer"] = "w4b-test", ["Jwt:Audience"] = "w4b-test",
            ["Erp:InventoryPosting:Enabled"] = enabled.ToString(), ["Erp:InventoryPosting:Scopes:0:TenantId"] = s.Tenant.ToString(),
            ["Erp:InventoryPosting:ReceiptsEnabled"] = receiptsEnabled.ToString(),
            ["Erp:InventoryPosting:TransfersEnabled"] = transfersEnabled.ToString(),
            ["Erp:InventoryPosting:WriteOffsEnabled"] = writeOffsEnabled.ToString(),
            ["Erp:InventoryPosting:PosEnabled"] = posEnabled.ToString(),
            ["Erp:InventoryPosting:PosReturnsEnabled"] = posReturnsEnabled.ToString(),
            ["Erp:InventoryPosting:ProductionEnabled"] = productionEnabled.ToString(),
            ["Erp:InventoryPosting:Scopes:0:LocationId"] = s.Location.ToString(),
        });
        if (extraLocation is Guid destination) builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
            ["Erp:InventoryPosting:Scopes:1:TenantId"] = s.Tenant.ToString(), ["Erp:InventoryPosting:Scopes:1:LocationId"] = destination.ToString() });
        builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Error);
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(InventoryPostingController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyControllers()));
        builder.Services.AddApplication(); builder.Services.AddInfrastructure(builder.Configuration);
        var original = builder.Services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)).ImplementationFactory!;
        builder.Services.Replace(ServiceDescriptor.Scoped<DbContextOptions<AppDbContext>>(sp =>
            new DbContextOptionsBuilder<AppDbContext>((DbContextOptions<AppDbContext>)original(sp)).IgnoreManyServiceProvidersWarning().Options));
        builder.Services.AddApiSecurity(builder.Configuration); builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        var app = builder.Build(); app.UseExceptionHandler(); app.UseRouting(); app.UseApiSecurity(); app.MapControllers(); await app.StartAsync(); return app;
    }
    private async Task Token(HttpClient client, InventoryFenceScenario s, DateTime? mfa)
    {
        // The two-argument overload uses a fresh proof; this overload also tests an explicit missing proof.
        await using var db = fixture.Context(); var user = await db.Users.SingleAsync(u => u.Id == s.Actor);
        var session = RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(1), authVersion: user.AuthVersion, mfaVerifiedAt: mfa);
        db.Add(session); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = Key,
            ["Jwt:Issuer"] = "w4b-test", ["Jwt:Audience"] = "w4b-test" }).Build();
        var jwt = new JwtService(config).GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, s.Location,
            sessionId: session.SessionId, authVersion: user.AuthVersion, mfaVerifiedAt: mfa);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    }
    private Task Token(HttpClient client, InventoryFenceScenario s) => Token(client, s, DateTime.UtcNow);
    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    private static Dictionary<string, object?> Body(InventoryFenceScenario s) => new()
    {
        ["requestId"] = Guid.NewGuid(), ["locationId"] = s.Location, ["batchId"] = s.Batch,
        ["expectedQuantity"] = "3.25", ["quantity"] = "5.00", ["reason"] = "Reviewed physical count",
    };
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
    private static async Task Code(HttpResponseMessage response, HttpStatusCode status, string code) =>
        Assert.Equal(code, (await Json(response, status)).GetProperty("code").GetString());
    [ErpPostgresFact]
    public async Task Receipt_HTTP_uses_reviewed_fingerprint_and_replays_one_receiving()
    {
        var r = await ReceiptPostingPostgresTests.Seed(fixture); var s = r.Inventory;
        await using var app = await Server(s); using var client = Client(app); await Token(client,s);
        var preview = await Json(await client.GetAsync($"/api/erp/receipts/{r.Receipt}/receiving"),HttpStatusCode.OK);
        Assert.True(preview.GetProperty("canReceive").GetBoolean()); Assert.Equal(r.Fingerprint,preview.GetProperty("fingerprint").GetString());
        var first = await Json(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint }),HttpStatusCode.OK);
        var replay = await Json(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint }),HttpStatusCode.OK);
        Assert.Equal("received",first.GetProperty("status").GetString()); Assert.False(first.GetProperty("erpReplayed").GetBoolean());
        Assert.True(replay.GetProperty("erpReplayed").GetBoolean()); Assert.Equal(first.GetProperty("erpPostingId").GetString(),replay.GetProperty("erpPostingId").GetString());
        await Code(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=new string('a',64) }),HttpStatusCode.Conflict,"receipt_posting_conflict");
    }
    [ErpPostgresTheory]
    [InlineData(-1)] [InlineData(-301)]
    public async Task Receipt_HTTP_requires_recent_MFA(int offset)
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture); await using var app=await Server(r.Inventory); using var client=Client(app);
        await Token(client,r.Inventory,offset==-1?null:DateTime.UtcNow.AddSeconds(offset));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint })).StatusCode);
    }
    [ErpPostgresFact]
    public async Task Receipt_HTTP_preserves_storekeeper_receiving_role_and_returns_no_foreign_receipts()
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture); var foreign=await ReceiptPostingPostgresTests.Seed(fixture);
        await using(var db=fixture.Context()) { var user=await db.Users.SingleAsync(x=>x.Id==r.Inventory.Actor); user.SetRole("storekeeper"); await db.SaveChangesAsync(); }
        await using var app=await Server(r.Inventory); using var client=Client(app); await Token(client,r.Inventory);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/erp/receipts/{foreign.Receipt}/receiving")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint })).StatusCode);
    }
    [ErpPostgresFact]
    public async Task Receipt_HTTP_gate_pause_does_not_enable_legacy_creation()
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture); await using var app=await Server(r.Inventory,false); using var client=Client(app); await Token(client,r.Inventory);
        await Code(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint }),HttpStatusCode.Conflict,"inventory_posting_disabled");
        await Code(await client.PutAsync($"/api/receipts/{r.Receipt}/receive",null),HttpStatusCode.Conflict,"inventory_scope_fenced");
        await using var db=fixture.Context(); Assert.Equal("ordered",(await db.StockReceipts.SingleAsync(x=>x.Id==r.Receipt)).Status);
        Assert.False(await db.ProductStocks.AnyAsync(x=>x.SourceId==r.Receipt));
    }
    [ErpPostgresFact]
    public async Task Receipt_HTTP_missing_fingerprint_is_400_and_disabled_module_is_403()
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture); await using var app=await Server(r.Inventory); using var client=Client(app); await Token(client,r.Inventory);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { })).StatusCode);
        await using(var db=fixture.Context()) { (await db.Tenants.SingleAsync(x=>x.Id==r.Inventory.Tenant)).UpdateModules(["inventory"]); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/erp/receipts/{r.Receipt}/receiving")).StatusCode);
    }

    [ErpPostgresFact]
    public async Task Receipt_HTTP_has_a_separate_default_off_writer_gate()
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture); await using var app=await Server(r.Inventory,receiptsEnabled:false);
        using var client=Client(app); await Token(client,r.Inventory);
        var preview=await Json(await client.GetAsync($"/api/erp/receipts/{r.Receipt}/receiving"),HttpStatusCode.OK);
        Assert.True(preview.GetProperty("fenced").GetBoolean()); Assert.False(preview.GetProperty("canReceive").GetBoolean());
        await Code(await client.PostAsJsonAsync($"/api/erp/receipts/{r.Receipt}/receive",new { expectedFingerprint=r.Fingerprint }),HttpStatusCode.Conflict,"inventory_posting_disabled");
    }

    [ErpPostgresFact]
    public async Task Transfer_HTTP_dispatch_receive_and_exact_replay_return_original_posting_ids()
    {
        var s=await TransferPostingPostgresTests.Seed(fixture); await using var app=await Server(s.Source,extraLocation:s.Destination);
        using var client=Client(app); await Token(client,s.Source);
        var preview=await Json(await client.PostAsJsonAsync("/api/erp/transfers/dispatch/preview",s.Command),HttpStatusCode.OK);
        var command=s.Command with { ExpectedFingerprint=preview.GetProperty("fingerprint").GetString() };
        var sent=await Json(await client.PostAsJsonAsync("/api/erp/transfers/dispatch",command),HttpStatusCode.OK);
        var replay=await Json(await client.PostAsJsonAsync("/api/erp/transfers/dispatch",command),HttpStatusCode.OK);
        Assert.Equal(sent.GetProperty("postingId").GetGuid(),replay.GetProperty("postingId").GetGuid()); Assert.True(replay.GetProperty("replayed").GetBoolean());
        var arrival=await Json(await client.GetAsync($"/api/erp/transfers/{command.RequestId}/receive/preview"),HttpStatusCode.OK);
        var body=new { expectedFingerprint=arrival.GetProperty("fingerprint").GetString() };
        var received=await Json(await client.PostAsJsonAsync($"/api/erp/transfers/{command.RequestId}/receive",body),HttpStatusCode.OK);
        var receivedAgain=await Json(await client.PostAsJsonAsync($"/api/erp/transfers/{command.RequestId}/receive",body),HttpStatusCode.OK);
        Assert.Equal(received.GetProperty("postingId").GetGuid(),receivedAgain.GetProperty("postingId").GetGuid());
    }
    [ErpPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transfer_HTTP_requires_recent_MFA_for_dispatch(bool expired)
    {
        var s=await TransferPostingPostgresTests.Seed(fixture); await using var app=await Server(s.Source,extraLocation:s.Destination);
        using var client=Client(app); await Token(client,s.Source,expired?DateTime.UtcNow.AddHours(-1):null);
        var preview=await Json(await client.PostAsJsonAsync("/api/erp/transfers/dispatch/preview",s.Command),HttpStatusCode.OK);
        await Code(await client.PostAsJsonAsync("/api/erp/transfers/dispatch",s.Command with { ExpectedFingerprint=preview.GetProperty("fingerprint").GetString() }),HttpStatusCode.Forbidden,"mfa_step_up_required");
    }
    [ErpPostgresFact]
    public async Task Transfer_HTTP_gate_off_does_not_fall_back_to_legacy_or_create_stock()
    {
        var s=await TransferPostingPostgresTests.Seed(fixture); await using var app=await Server(s.Source,extraLocation:s.Destination,transfersEnabled:false);
        using var client=Client(app); await Token(client,s.Source);
        var preview=await Json(await client.PostAsJsonAsync("/api/erp/transfers/dispatch/preview",s.Command),HttpStatusCode.OK); Assert.False(preview.GetProperty("canPost").GetBoolean());
        await Code(await client.PostAsJsonAsync("/api/erp/transfers/dispatch",s.Command with { ExpectedFingerprint=preview.GetProperty("fingerprint").GetString() }),HttpStatusCode.Conflict,"inventory_posting_disabled");
    }
    [ErpPostgresFact]
    public async Task Transfer_HTTP_invalid_fingerprint_is_400_and_foreign_transfer_is_404()
    {
        var s=await TransferPostingPostgresTests.Seed(fixture); await using var app=await Server(s.Source,extraLocation:s.Destination);
        using var client=Client(app); await Token(client,s.Source);
        await Code(await client.PostAsJsonAsync("/api/erp/transfers/dispatch",s.Command),HttpStatusCode.BadRequest,"invalid_transfer_posting");
        await Code(await client.GetAsync($"/api/erp/transfers/{Guid.NewGuid()}/receive/preview"),HttpStatusCode.NotFound,"transfer_not_found");
    }
    [ErpPostgresFact]
    public async Task WriteOff_HTTP_preview_returns_the_exact_source_and_approval_replays()
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);await using var app=await Server(s.Inventory);using var client=Client(app);await Token(client,s.Inventory);
        var preview=await Json(await client.GetAsync($"/api/erp/write-offs/{s.WriteOffId}/approve/preview"),HttpStatusCode.OK);
        Assert.Equal("expired",preview.GetProperty("source").GetProperty("reason").GetString());Assert.Equal(45m,preview.GetProperty("source").GetProperty("totalLossAmount").GetDecimal());
        var body=new{expectedFingerprint=preview.GetProperty("fingerprint").GetString()};
        var posted=await Json(await client.PostAsJsonAsync($"/api/erp/write-offs/{s.WriteOffId}/approve",body),HttpStatusCode.OK);
        var replay=await Json(await client.PostAsJsonAsync($"/api/erp/write-offs/{s.WriteOffId}/approve",body),HttpStatusCode.OK);
        Assert.Equal(posted.GetProperty("postingId").GetGuid(),replay.GetProperty("postingId").GetGuid());Assert.True(replay.GetProperty("replayed").GetBoolean());
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task WriteOff_HTTP_requires_recent_MFA(bool expired)
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);await using var app=await Server(s.Inventory);using var client=Client(app);await Token(client,s.Inventory,expired?DateTime.UtcNow.AddHours(-1):null);
        var body=await WriteOffPostingPostgresTests.Review(s);
        await Code(await client.PostAsJsonAsync($"/api/erp/write-offs/{s.WriteOffId}/approve",body),HttpStatusCode.Forbidden,"mfa_step_up_required");
    }
    [ErpPostgresFact]
    public async Task WriteOff_HTTP_default_off_writer_gate_is_independent()
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);await using var app=await Server(s.Inventory,writeOffsEnabled:false);using var client=Client(app);await Token(client,s.Inventory);
        var preview=await Json(await client.GetAsync($"/api/erp/write-offs/{s.WriteOffId}/approve/preview"),HttpStatusCode.OK);Assert.False(preview.GetProperty("canPost").GetBoolean());
        await Code(await client.PostAsJsonAsync($"/api/erp/write-offs/{s.WriteOffId}/approve",new{expectedFingerprint=preview.GetProperty("fingerprint").GetString()}),HttpStatusCode.Conflict,"inventory_posting_disabled");
    }
    [ErpPostgresFact]
    public async Task WriteOff_HTTP_rejection_is_a_review_not_an_inventory_posting()
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);await using var app=await Server(s.Inventory);using var client=Client(app);await Token(client,s.Inventory);
        var body=await WriteOffPostingPostgresTests.Review(s,"reject","Additional physical inspection required");
        var result=await Json(await client.PostAsJsonAsync($"/api/erp/write-offs/{s.WriteOffId}/reject",body),HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null,result.GetProperty("postingId").ValueKind);
    }
    [ErpPostgresFact]
    public async Task WriteOff_HTTP_storekeeper_cannot_approve_and_foreign_record_is_not_found()
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);var foreign=await WriteOffPostingPostgresTests.Seed(fixture);
        await using var app=await Server(s.Inventory);using var client=Client(app);await Token(client,s.Inventory);
        await Code(await client.GetAsync($"/api/erp/write-offs/{foreign.WriteOffId}/approve/preview"),HttpStatusCode.NotFound,"writeoff_not_found");
        var user=User.Create(s.Inventory.Tenant,Guid.NewGuid()+"@test.local","Synthetic keeper","hash","storekeeper");
        await using(var db=fixture.Context()){db.AddRange(user,UserLocation.Create(s.Inventory.Tenant,user.Id,s.Inventory.Location,null));await db.SaveChangesAsync();}
        await Token(client,s.Inventory with{Actor=user.Id});
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/erp/write-offs/{s.WriteOffId}/approve/preview")).StatusCode);
    }
    [ErpPostgresFact]
    public async Task POS_HTTP_sale_uses_the_registered_bridge_and_exact_replay_without_new_client_UI()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);await using var app=await Server(s.S,posEnabled:true);using var client=Client(app);await Token(client,s.S);
        var body=s.Request();var a=await Json(await client.PostAsJsonAsync("/api/pos/sales",body),HttpStatusCode.Created);
        var b=await Json(await client.PostAsJsonAsync("/api/pos/sales",body),HttpStatusCode.Created);Assert.Equal(a.GetProperty("transactionId").GetString(),b.GetProperty("transactionId").GetString());
        await using var db=fixture.Context();Assert.Equal(1,await db.BusinessDocuments.CountAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem=="pos.sale.v1"));
    }
    [ErpPostgresFact]
    public async Task POS_HTTP_default_off_gate_blocks_fenced_sale_and_leaves_stock_unchanged()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);await using var app=await Server(s.S);using var client=Client(app);await Token(client,s.S);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/pos/sales",s.Request())).StatusCode);
        await using var db=fixture.Context();Assert.False(await db.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task POS_HTTP_unverified_legacy_offline_batch_requires_reconciliation_without_repricing()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);await using var app=await Server(s.S,posEnabled:true);using var client=Client(app);await Token(client,s.S);
        var body=new {sales=new[]{s.Request()}};
        var a=await Json(await client.PostAsJsonAsync("/api/pos/sales/batch",body),HttpStatusCode.OK);
        Assert.Equal("failed",a.GetProperty("results")[0].GetProperty("status").GetString());Assert.Equal(409,a.GetProperty("results")[0].GetProperty("errorStatusCode").GetInt32());
        await using var db=fixture.Context();Assert.False(await db.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
    }
    [ErpPostgresFact]
    public async Task Full_POS_return_HTTP_posts_replays_and_preserves_restricted_runtime_RLS()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await PosReturnPostgresTests.Sale(fixture,s);
        await using var app=await Server(s.S,posReturnsEnabled:true);using var client=Client(app);await Token(client,s.S);
        var p=await Json(await client.GetAsync($"/api/erp/pos-returns/{sale}/preview?shiftId={s.Shift}"),HttpStatusCode.OK);
        var body=new{shiftId=s.Shift,clientRequestId=Guid.NewGuid(),expectedFingerprint=p.GetProperty("fingerprint").GetString(),reason="Customer returned the entire purchased receipt"};
        var first=await Json(await client.PostAsJsonAsync($"/api/erp/pos-returns/{sale}",body),HttpStatusCode.OK);
        var replay=await Json(await client.PostAsJsonAsync($"/api/erp/pos-returns/{sale}",body),HttpStatusCode.OK);
        Assert.Equal(first.GetProperty("documentId").GetGuid(),replay.GetProperty("documentId").GetGuid());Assert.True(replay.GetProperty("replayed").GetBoolean());
    }
    [ErpPostgresTheory][InlineData(false)][InlineData(true)]
    public async Task Full_POS_return_HTTP_requires_recent_MFA(bool expired)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await PosReturnPostgresTests.Sale(fixture,s);
        await using var app=await Server(s.S,posReturnsEnabled:true);using var client=Client(app);await Token(client,s.S,expired?DateTime.UtcNow.AddHours(-1):null);
        await Code(await client.PostAsJsonAsync($"/api/erp/pos-returns/{sale}",new{shiftId=s.Shift,clientRequestId=Guid.NewGuid(),expectedFingerprint=new string('a',64),reason="Customer returned the entire purchased receipt"}),HttpStatusCode.Forbidden,"mfa_step_up_required");
    }
    [ErpPostgresFact]
    public async Task Full_POS_return_HTTP_default_off_and_foreign_sale_are_denied()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await PosReturnPostgresTests.Sale(fixture,s);
        var other=await PosInventoryPostingPostgresTests.Seed(fixture);var foreign=await PosReturnPostgresTests.Sale(fixture,other);
        await using var app=await Server(s.S);using var client=Client(app);await Token(client,s.S);
        await Code(await client.GetAsync($"/api/erp/pos-returns/{foreign}/preview?shiftId={s.Shift}"),HttpStatusCode.NotFound,"pos_return_not_found");
        var p=await Json(await client.GetAsync($"/api/erp/pos-returns/{sale}/preview?shiftId={s.Shift}"),HttpStatusCode.OK);Assert.False(p.GetProperty("canPost").GetBoolean());
        await Code(await client.PostAsJsonAsync($"/api/erp/pos-returns/{sale}",new{shiftId=s.Shift,clientRequestId=Guid.NewGuid(),expectedFingerprint=p.GetProperty("fingerprint").GetString(),reason="Customer returned the entire purchased receipt"}),HttpStatusCode.Conflict,"inventory_posting_disabled");
    }
    [ErpPostgresFact]
    public async Task Production_review_HTTP_uses_live_RLS_and_is_read_only_even_when_posting_is_available()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S,productionEnabled:true);using var client=Client(app);await Token(client,s.S,null);
        var p=await Json(await client.GetAsync($"/api/erp/production/{s.OrderId}/preview"),HttpStatusCode.OK);
        Assert.True(p.GetProperty("reviewable").GetBoolean());Assert.True(p.GetProperty("configured").GetBoolean());
        Assert.True(p.GetProperty("writerImplemented").GetBoolean());Assert.True(p.GetProperty("canPost").GetBoolean());Assert.Equal(2,p.GetProperty("materials").GetArrayLength());
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.PostAsJsonAsync($"/api/erp/production/{s.OrderId}/preview",new{})).StatusCode);
        await using var db=fixture.Context();Assert.Equal(7.25m,await db.ProductStocks.Where(b=>b.TenantId==s.S.Tenant).SumAsync(b=>b.Quantity));
        Assert.False(await db.ProductionOrderConsumptions.AnyAsync(c=>c.ProductionOrderId==s.OrderId));
    }
    [ErpPostgresFact]
    public async Task Production_review_HTTP_foreign_order_is_not_found()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);var other=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S);using var client=Client(app);await Token(client,s.S);
        await Code(await client.GetAsync($"/api/erp/production/{other.OrderId}/preview"),HttpStatusCode.NotFound,"production_order_not_found");
    }
    [ErpPostgresFact]
    public async Task Production_review_HTTP_revoked_capability_cannot_be_used_from_a_stale_token()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S);using var client=Client(app);await Token(client,s.S);
        await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"production.complete",false}});await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/erp/production/{s.OrderId}/preview")).StatusCode);
    }
    [ErpPostgresFact]
    public async Task Production_HTTP_confirmation_posts_and_replays_one_output_under_real_RLS()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S,productionEnabled:true);using var client=Client(app);await Token(client,s.S);
        var preview=await Json(await client.GetAsync($"/api/erp/production/{s.OrderId}/preview"),HttpStatusCode.OK);
        var c=new{requestId=Guid.NewGuid(),expectedFingerprint=preview.GetProperty("fingerprint").GetString(),reason="Reviewed production issue and finished output"};
        var a=await Json(await client.PostAsJsonAsync($"/api/erp/production/{s.OrderId}/complete",c),HttpStatusCode.OK);
        var b=await Json(await client.PostAsJsonAsync($"/api/erp/production/{s.OrderId}/complete",c),HttpStatusCode.OK);
        Assert.False(a.GetProperty("replayed").GetBoolean());Assert.True(b.GetProperty("replayed").GetBoolean());Assert.Equal(a.GetProperty("postingId").GetGuid(),b.GetProperty("postingId").GetGuid());
    }
    [ErpPostgresTheory][InlineData(false)][InlineData(true)]
    public async Task Production_HTTP_mutation_requires_fresh_MFA(bool expired)
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S,productionEnabled:true);using var client=Client(app);await Token(client,s.S,expired?DateTime.UtcNow.AddHours(-1):null);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/erp/production/{s.OrderId}/complete",new{requestId=Guid.NewGuid(),expectedFingerprint=new string('a',64),reason="Reviewed production issue and finished output"})).StatusCode);
    }
    [ErpPostgresFact]
    public async Task Production_HTTP_default_disabled_and_foreign_order_fail_without_writes()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);var other=await ProductionPostingPreviewPostgresTests.Seed(fixture);
        await using var app=await Server(s.S);using var client=Client(app);await Token(client,s.S);
        var c=new{requestId=Guid.NewGuid(),expectedFingerprint=new string('a',64),reason="Reviewed production issue and finished output"};
        await Code(await client.PostAsJsonAsync($"/api/erp/production/{s.OrderId}/complete",c),HttpStatusCode.Conflict,"inventory_posting_disabled");
        await Code(await client.PostAsJsonAsync($"/api/erp/production/{other.OrderId}/complete",c),HttpStatusCode.NotFound,"production_order_not_found");
    }
    private sealed class OnlyControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var type in feature.Controllers.Where(t => t.AsType() != typeof(InventoryPostingController) && t.AsType() != typeof(StockController)
                         && t.AsType() != typeof(ReceiptPostingController) && t.AsType() != typeof(ReceiptsController)
                         && t.AsType() != typeof(TransferPostingController) && t.AsType() != typeof(TransfersController)
                         && t.AsType() != typeof(WriteOffPostingController) && t.AsType() != typeof(WriteOffsController) && t.AsType() != typeof(PosController) && t.AsType() != typeof(PosReturnPostingController) && t.AsType() != typeof(ProductionPostingPreviewController) && t.AsType() != typeof(ProductionPostingController)).ToList())
                feature.Controllers.Remove(type);
        }
    }
}
