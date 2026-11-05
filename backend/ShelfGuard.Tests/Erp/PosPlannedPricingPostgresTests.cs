using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class PosPlannedPricingPostgresTests(ErpSchemaFixture fixture)
{
    private sealed class BrokenSource : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries<PosPricingSnapshot>().Where(e => e.State == EntityState.Added))
            {
                var json = JsonNode.Parse(entry.Entity.Payload)!;
                json["lines"]![0]!["baseResolution"]!["version"] = 2;
                var payload = json.ToJsonString();
                entry.Property(a => a.Payload).CurrentValue = payload;
                entry.Property(a => a.ContentHash).CurrentValue = PosPricingCapture.Hash(payload);
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Access(WorkAccessScope s) : IWorkAccessContext
    { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(s); }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PricingDesktopStore Store(PosInventoryPostingPostgresTests.Scenario s)
    {
        using var db = fixture.Context();
        return new((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>(), new Access(s.Scope), TimeProvider.System);
    }
    private async Task<PricingSave> Command(PosInventoryPostingPostgresTests.Scenario s, decimal price,
        Guid? location = null, bool global = false)
    {
        await using var db = fixture.Context(); var item = await db.Items.SingleAsync(i => i.Id == s.S.Item);
        var at = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return new(Guid.NewGuid(), null, null, "Reviewed synthetic online POS price", new("P3 acceptance", global ? null : location ?? s.S.Location,
            10, true, at.AddDays(-1), at.AddDays(1), [new(item.Id, price, CatalogDesktopStore.View(item).Fingerprint)]));
    }
    private static PosBasePriceResolver Resolver(AppDbContext db, PosInventoryPostingPostgresTests.Scenario s,
        bool inventory = true, bool pos = true) => new(db,
        new PosOnlinePricingGate(true, [new(s.S.Tenant, s.S.Location)]),
        new InventoryPostingGate(inventory, [new(s.S.Tenant, s.S.Location)]), new(pos));
    private static async Task<PosBasePriceItem> Input(AppDbContext db, Guid id)
    {
        var i = await db.Items.AsNoTracking().SingleAsync(i => i.Id == id);
        return new(i.Id, i.Unit, i.PriceRetail, i.VatRate);
    }
    [ErpPostgresFact] public async Task Planned_online_sale_commits_selected_version_tax_stock_ledger_and_immutable_archive()
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture); var list = await Store(s).SaveAsync(await Command(s, 30), default);
        await using (var db = fixture.Context())
        {
            var request = s.Request(); var r = await PosInventoryPostingPostgresTests.Service(db, s, basePrices: Resolver(db, s))
                .CreateSaleAsync(s.S.Tenant, s.S.Actor, request);
            Assert.Null(r.Error); Assert.Equal(30, r.Sale!.Subtotal);
            var archive = await db.Set<PosPricingSnapshot>().SingleAsync(a => a.TransactionId == r.Sale.TransactionId);
            var d = JsonSerializer.Deserialize<PricingArchivedDecision>(archive.Payload, Json)!;
            Assert.Equal(list.Id, d.Lines.Single().BaseResolution!.PriceListId); Assert.Equal(list.Version, d.Lines[0].BaseResolution!.Version);
            Assert.Equal(50, d.Lines[0].BaseResolution!.LegacyPrice); Assert.Equal(20, d.Lines[0].BaseResolution!.LegacyVatRate);
            Assert.Equal("planned_price_list", d.Lines[0].BaseResolution!.Source); Assert.Equal(5, d.Result.Vat);
            Assert.Equal(2.25m, (await db.ProductStocks.SingleAsync(b => b.Id == s.S.Batch)).Quantity);
            Assert.Single(await db.InventoryLedgerEntries.Where(e => e.TenantId == s.S.Tenant && e.QuantityDelta == -1).ToArrayAsync());
            Assert.Equal(50, (await db.Items.SingleAsync(i => i.Id == s.S.Item)).PriceRetail);
            var replay = await PosInventoryPostingPostgresTests.Service(db, s).CreateSaleAsync(s.S.Tenant, s.S.Actor, request);
            Assert.Equal(r.Sale.TransactionId, replay.Sale!.TransactionId);
        }
    }
    [ErpPostgresFact] public async Task Inactive_new_revision_masks_prior_version_and_boundary_uses_current_revision()
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture); var store = Store(s); var c = await Command(s, 30);
        var saved = await store.SaveAsync(c, default); var prior = await store.ListAsync(saved.ListKey, default);
        await store.SaveAsync(c with { OperationId = Guid.NewGuid(), ListKey = saved.ListKey,
            ExpectedFingerprint = prior.Fingerprint, Data = c.Data with { IsActive = false } }, default);
        await using var db = fixture.Context(); var result = await Resolver(db, s).ResolveAsync(s.S.Tenant, s.S.Location,
            DateTime.UtcNow, [await Input(db, s.S.Item)]);
        Assert.Equal("legacy_item_base", result[s.S.Item].Source); Assert.Equal(50, result[s.S.Item].Price);
        var end = c.Data.ValidUntil!.Value.UtcDateTime;
        var expired = await Resolver(db, s).ResolveAsync(s.S.Tenant, s.S.Location, end, [await Input(db, s.S.Item)]);
        Assert.Equal(50, expired[s.S.Item].Price);
    }
    [ErpPostgresFact] public async Task Committed_source_version_control_is_a_diagnostic_mismatch_even_when_all_money_matches()
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture);
        await Store(s).SaveAsync(await Command(s, 30), default);
        Guid sale;
        await using (var db = fixture.Context(new BrokenSource()))
        {
            var r = await PosInventoryPostingPostgresTests.Service(db, s, basePrices: Resolver(db, s))
                .CreateSaleAsync(s.S.Tenant, s.S.Actor, s.Request());
            Assert.Null(r.Error); sale = r.Sale!.TransactionId; Assert.Equal(30, r.Sale.Subtotal);
        }
        await using var check = fixture.Context();
        var history = new PosPricingHistoryStore((DbContextOptions<AppDbContext>)check.GetService<IDbContextOptions>(), new Access(s.Scope));
        var result = await history.SnapshotAsync(sale, default);
        Assert.Equal("mismatch", result.Comparison.Outcome); Assert.Contains("base_resolution", result.Comparison.Differences);
        var counts = await history.DiagnosticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), s.S.Location, 1, default);
        Assert.Equal(1, counts.Evaluated); Assert.Equal(1, Assert.Single(counts.Outcomes).Count);
        Assert.Equal("mismatch", counts.Outcomes[0].Outcome);
    }
    [ErpPostgresTheory] [InlineData("gate")] [InlineData("writer")] [InlineData("fence")]
    public async Task Activation_requires_inventory_configuration_writer_and_integrated_database_fence(string failure)
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture, fenced: failure != "fence");
        await using var db = fixture.Context(); var input = await Input(db, s.S.Item);
        var error = await Assert.ThrowsAsync<PosPricingAuthorityException>(() => Resolver(db, s, failure != "gate", failure != "writer")
            .ResolveAsync(s.S.Tenant, s.S.Location, DateTime.UtcNow, [input]));
        Assert.Equal("pricing_inventory_not_ready", error.Code);
        Assert.Empty(await db.PosTransactions.Where(t => t.TenantId == s.S.Tenant).ToArrayAsync());
    }
    [ErpPostgresTheory] [InlineData("missing")] [InlineData("unit")] [InlineData("inactive")]
    public async Task Invalid_source_fails_without_legacy_zero_or_silent_fallback(string failure)
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture);
        if (failure != "missing") await Store(s).SaveAsync(await Command(s, 30), default);
        await using var db = fixture.Context(); var item = await db.Items.SingleAsync(i => i.Id == s.S.Item);
        if (failure == "missing") item.PriceRetail = null;
        if (failure == "unit") item.Unit = "litre";
        if (failure == "inactive") item.IsActive = false;
        await db.SaveChangesAsync();
        var result = await PosInventoryPostingPostgresTests.Service(db, s, basePrices: Resolver(db, s)).CreateSaleAsync(s.S.Tenant, s.S.Actor, s.Request());
        Assert.Equal(409, result.StatusCode); Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(b => b.Id == s.S.Batch)).Quantity);
        Assert.Empty(await db.PosTransactions.Where(t => t.TenantId == s.S.Tenant).ToArrayAsync());
    }
    [ErpPostgresFact] public async Task Zero_planned_price_is_valid_and_runtime_RLS_cannot_resolve_foreign_tenant()
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture); var other = await PosInventoryPostingPostgresTests.Seed(fixture);
        await Store(s).SaveAsync(await Command(s, 0), default);
        await using var db = fixture.Context(); var input = await Input(db, s.S.Item); var foreignInput = await Input(db, other.S.Item);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL ROLE " + fixture.ReaderRole);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{s.S.Tenant.ToString()},true)");
        var result = await Resolver(db, s).ResolveAsync(s.S.Tenant, s.S.Location, DateTime.UtcNow, [input]);
        Assert.Equal(0, result[s.S.Item].Price);
        await Assert.ThrowsAsync<PosPricingAuthorityException>(() => Resolver(db, other).ResolveAsync(other.S.Tenant, other.S.Location, DateTime.UtcNow, [foreignInput]));
    }
}
