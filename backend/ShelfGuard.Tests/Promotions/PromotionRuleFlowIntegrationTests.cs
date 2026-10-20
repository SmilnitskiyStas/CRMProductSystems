using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Promotions;

/// <summary>
/// Real-Postgres flow for the Phase 2 service: create -> validate -> publish -> visible in
/// GET /api/pos/sync/promotions (PosSyncService) -> pause -> gone -> resume -> edit published
/// (v2 draft, published v1 untouched) -> superseded draft deletion under the immutability trigger.
/// Connects as the crm superuser (SQL/EF correctness, not isolation); soft-skips without Postgres
/// or without the AddPromotionRules migration.
/// </summary>
public sealed class PromotionRuleFlowIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _cs = string.Empty;
    private bool _ready;
    private Guid _tenantId;
    private Guid _adminId;
    private readonly Guid _storeId = Guid.NewGuid();
    private Guid _itemId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    public PromotionRuleFlowIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cs = TestPostgres.ResolveConnectionString();
        try
        {
            await using var probe = new NpgsqlConnection(_cs);
            await probe.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'promotion_rule_versions'", probe);
            if ((long)(await cmd.ExecuteScalarAsync())! == 0)
            {
                _output.WriteLine("Skipping - AddPromotionRules migration not applied.");
                return;
            }
            _ready = true;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Skipping - no Postgres at '{_cs}': {ex.Message}");
            return;
        }

        await using var db = NewContext();
        var tenant = Tenant.Create($"Promo Svc {_run}", $"promo-svc-{_run}");
        tenant.UpdateModules(["inventory", "pos"]);
        _tenantId = tenant.Id;
        var admin = User.Create(tenant.Id, $"admin-{_run}@promo.test", "Promo Admin", "pw", AppRoles.EnterpriseAdmin, null);
        _adminId = admin.Id;
        var item = new Item { TenantId = tenant.Id, Name = "Svc Item", Barcodes = [$"SV-{_run}"[..20]], PriceRetail = 30m };
        _itemId = item.Id;
        db.Tenants.Add(tenant);
        db.Locations.Add(new Location { Id = _storeId, TenantId = tenant.Id, Name = "Svc Store" });
        db.Users.Add(admin);
        db.Items.Add(item);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_ready) return;
        await using var db = NewContext();
        foreach (var sql in new[]
        {
            "DELETE FROM promotion_rules WHERE \"TenantId\" = {0}",
            "DELETE FROM items WHERE \"TenantId\" = {0}",
            "DELETE FROM users WHERE \"TenantId\" = {0}",
            "DELETE FROM locations WHERE \"TenantId\" = {0}",
            "DELETE FROM tenants WHERE \"Id\" = {0}",
        })
        {
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(sql, _tenantId);
#pragma warning restore EF1002
        }
    }

    [Fact]
    public async Task Create_validate_publish_sync_pause_resume_and_edit_flow()
    {
        if (!_ready) { _output.WriteLine("skipped"); return; }
        var caller = new PromotionRuleCaller(_tenantId, _adminId, AppRoles.EnterpriseAdmin, null);

        var definition = JsonSerializer.SerializeToElement(new PromotionDefinition
        {
            Type = PromotionRuleTypes.BuyNGetM,
            Targets = new PromotionTargets { ItemIds = [_itemId] },
            Scope = new PromotionScope { StoreIds = [_storeId] },
            Params = new PromotionParams { BuyQty = 2, GetQty = 1, GetPrice = 0.01m },
        }, PromotionDefinitionJson.Options);

        // create (draft, v1)
        var created = (await WithService(s => s.CreateAsync(caller, new CreatePromotionRuleRequest("2+1 svc", "d", definition)))).Value!;
        Assert.Equal(("draft", 1, "Promo Admin"), (created.Status, created.CurrentVersion.VersionNumber, created.CurrentVersion.CreatedByName));
        Assert.Empty(await SyncedRuleIdsAsync()); // drafts never reach the cashier

        // validate
        var validation = (await WithService(s => s.ValidateAsync(caller, created.Id))).Value!;
        Assert.Empty(validation.Errors);

        // publish -> shows in sync
        var published = (await WithService(s => s.PublishAsync(caller, created.Id))).Value!;
        Assert.Equal("published", published.Status);
        Assert.Contains(created.Id, await SyncedRuleIdsAsync());

        // pause -> disappears; resume -> back
        Assert.Equal("paused", (await WithService(s => s.PauseAsync(caller, created.Id))).Value!.Status);
        Assert.DoesNotContain(created.Id, await SyncedRuleIdsAsync());
        Assert.Equal("published", (await WithService(s => s.ResumeAsync(caller, created.Id))).Value!.Status);
        Assert.Contains(created.Id, await SyncedRuleIdsAsync());

        // edit the published rule: v2 is a draft, v1 stays live in sync
        var edited = JsonSerializer.SerializeToElement(new PromotionDefinition
        {
            Type = PromotionRuleTypes.PercentOff,
            Targets = new PromotionTargets { ItemIds = [_itemId] },
            Scope = new PromotionScope { StoreIds = [_storeId] },
            Params = new PromotionParams { Percent = 15m },
        }, PromotionDefinitionJson.Options);
        var v2 = (await WithService(s => s.UpdateAsync(caller, created.Id, new UpdatePromotionRuleRequest("2+1 svc", "d", edited, "to percent")))).Value!;
        Assert.Equal((2, 1), (v2.CurrentVersion.VersionNumber, v2.PublishedVersion!.VersionNumber));
        Assert.Equal("buy_n_get_m", (await SyncedDefinitionAsync(created.Id)).GetProperty("type").GetString());

        // editing the unpublished v2 again replaces it (still v2) via delete + insert despite the UPDATE trigger
        var v3 = (await WithService(s => s.UpdateAsync(caller, created.Id, new UpdatePromotionRuleRequest("2+1 svc", "d", edited, null)))).Value!;
        Assert.Equal(2, v3.CurrentVersion.VersionNumber);
        Assert.Equal([2, 1], v3.Versions.Select(v => v.VersionNumber).ToArray());

        // publish v3 -> sync now serves the percent rule
        await WithService(s => s.PublishAsync(caller, created.Id));
        Assert.Equal("percent_off", (await SyncedDefinitionAsync(created.Id)).GetProperty("type").GetString());

        // archive -> gone
        await WithService(s => s.ArchiveAsync(caller, created.Id));
        Assert.DoesNotContain(created.Id, await SyncedRuleIdsAsync());

        // list shows it as archived with both versions kept
        var listed = Assert.Single(await WithService(s => s.ListAsync(caller, "archived", null)));
        Assert.Equal(("archived", 2, 2), (listed.Status, listed.CurrentVersionNumber, listed.PublishedVersionNumber!.Value));
    }

    private async Task<List<Guid>> SyncedRuleIdsAsync()
    {
        await using var db = NewContext();
        var sync = new PosSyncService(new CatalogSyncRepository(db), new PromotionRuleRepository(db), new CashRegisterRepository(db));
        var (res, err, _) = await sync.GetPromotionsAsync(_tenantId, null, _storeId, null);
        Assert.Null(err);
        return res!.Rules.Select(r => r.Id).ToList();
    }

    private async Task<JsonElement> SyncedDefinitionAsync(Guid ruleId)
    {
        await using var db = NewContext();
        var sync = new PosSyncService(new CatalogSyncRepository(db), new PromotionRuleRepository(db), new CashRegisterRepository(db));
        var (res, _, _) = await sync.GetPromotionsAsync(_tenantId, null, _storeId, null);
        return res!.Rules.Single(r => r.Id == ruleId).Definition;
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_cs);

    private async Task<T> WithService<T>(Func<PromotionRuleService, Task<T>> action)
    {
        await using var db = NewContext();
        var svc = new PromotionRuleService(
            new PromotionRuleRepository(db), new ItemRepository(db), new CategoryRepository(db),
            new LocationRepository(db), new LoyaltyRepository(db), new UserRepository(db), new UserLocationRepository(db));
        return await action(svc);
    }
}
