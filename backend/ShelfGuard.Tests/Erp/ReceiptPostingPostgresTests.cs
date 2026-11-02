using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ReceiptPostingPostgresTests(ErpSchemaFixture fixture)
{
    internal sealed record Scenario(InventoryFenceScenario Inventory, Guid Receipt, string Fingerprint)
    {
        internal WorkAccessScope Scope => new(Inventory.Tenant, Inventory.Actor, "store_manager", false, [Inventory.Location], new HashSet<string>(), new Dictionary<string, bool>());
        internal IInventoryPostingGate Gate(bool enabled = true) => new InventoryPostingGate(enabled, [new(Inventory.Tenant, Inventory.Location)]);
    }
    internal static async Task<Scenario> Seed(ErpSchemaFixture fixture, int lines = 2, bool fence = true)
    {
        var s = await InventoryFenceScenario.Seed(fixture, fence);
        await using var db = fixture.Context();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == s.Tenant); tenant.UpdateModules(["inventory", "procurement"]);
        var r = new StockReceipt { TenantId = s.Tenant, DestinationStoreId = s.Location, Status = "ordered", CreatedBy = s.Actor };
        for (var i = 0; i < lines; i++) r.Items.Add(new StockReceiptItem { ReceiptId = r.Id, ProductId = s.Item,
            QuantityOrdered = 2.25m + i, PricePurchase = 12.50m, BatchNumber = "incoming-" + i, ExpiryDate = new(2027, 11, i + 1) });
        db.Add(r); await db.SaveChangesAsync();
        var loaded = await db.StockReceipts.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Product).SingleAsync(x => x.Id == r.Id);
        return new(s, r.Id, ReceiptPostingRules.Fingerprint(loaded));
    }
    internal static ReceiptPostingStore Store(ErpSchemaFixture fixture, Scenario s, bool enabled = true, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestPostgres.DataSource(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return new(options.Options, s.Gate(enabled), TimeProvider.System, new(true));
    }
    [ErpPostgresFact]
    public async Task Multi_line_receive_commits_graph_prices_notification_and_replays_without_new_batches()
    {
        var s = await Seed(fixture); var store = Store(fixture, s);
        var preview = await store.PreviewAsync(s.Scope, s.Receipt, default); Assert.True(preview.CanReceive); Assert.Equal(s.Fingerprint, preview.Fingerprint);
        var first = await store.ReceiveAsync(s.Scope, s.Receipt, s.Fingerprint, default);
        var replay = await store.ReceiveAsync(s.Scope, s.Receipt, s.Fingerprint, default);
        Assert.False(first.Replayed); Assert.Equal(first with { Replayed = true }, replay);
        await using var db = fixture.Context();
        var stocks = await db.ProductStocks.Where(x => x.SourceId == s.Receipt).OrderBy(x => x.BatchNumber).ToListAsync();
        Assert.Equal(2, stocks.Count); Assert.Equal(2.25m, stocks[0].Quantity); Assert.Equal(3.25m, stocks[1].Quantity);
        Assert.Equal(new DateOnly(2027, 11, 1), stocks[0].ExpiryDate);
        Assert.Equal(2, await db.InventoryLedgerEntries.CountAsync(x => x.PostingId == first.PostingId));
        Assert.Equal(2, await db.InventoryBalanceProjections.CountAsync(x => x.TenantId == s.Inventory.Tenant && stocks.Select(x => x.Id).Contains(x.BatchId)));
        var movements = await db.StockMovements.Where(x => x.ReferenceId == s.Receipt).OrderBy(x => x.Quantity).ToListAsync();
        Assert.Equal(28.13m, movements[0].TotalAmount); Assert.Equal(12.50m, movements[0].UnitPrice);
        Assert.Single(await db.NotificationQueues.Where(x => x.TenantId == s.Inventory.Tenant && x.EventType == "receipt.created").ToListAsync());
        Assert.Equal("received", (await db.StockReceipts.SingleAsync(x => x.Id == s.Receipt)).Status);
        Assert.Equal("posted", (await db.BusinessDocuments.SingleAsync(x => x.Id == first.DocumentId)).Status);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => store.ReceiveAsync(s.Scope, s.Receipt, new string('a',64), default));
    }
    [ErpPostgresFact]
    public async Task Two_operators_racing_the_same_receipt_create_one_posting()
    {
        var s = await Seed(fixture);
        var other=User.Create(s.Inventory.Tenant,Guid.NewGuid()+"@test.local","Receiver","hash","storekeeper");
        await using(var db=fixture.Context()) { db.AddRange(other,UserLocation.Create(s.Inventory.Tenant,other.Id,s.Inventory.Location,null)); await db.SaveChangesAsync(); }
        var results = await Task.WhenAll(Store(fixture, s).ReceiveAsync(s.Scope, s.Receipt, s.Fingerprint, default),
            Store(fixture, s).ReceiveAsync(s.Scope with { UserId=other.Id,Role="storekeeper" }, s.Receipt, s.Fingerprint, default));
        Assert.Equal(results[0].PostingId, results[1].PostingId); Assert.Single(results, x => x.Replayed);
    }
    [ErpPostgresFact]
    public async Task Stale_fingerprint_after_line_edit_never_creates_stock()
    {
        var s = await Seed(fixture); await using (var db = fixture.Context()) { var line = await db.StockReceiptItems.FirstAsync(x => x.ReceiptId == s.Receipt); line.QuantityReceived = 1; await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default)); await Empty(s);
    }
    [ErpPostgresTheory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task Gate_and_fence_are_both_required(bool gate, bool fence)
    {
        var s = await Seed(fixture, fence: fence); var store = Store(fixture,s,gate);
        var p = await store.PreviewAsync(s.Scope,s.Receipt,default); Assert.False(p.CanReceive);
        var failure = await Record.ExceptionAsync(() => store.ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default));
        Assert.True(failure is InventoryPostingDisabledException or InventoryPostingConflictException); await Empty(s);
    }
    [ErpPostgresFact]
    public async Task Foreign_receipt_or_location_assignment_cannot_post()
    {
        var s = await Seed(fixture); var other = await Seed(fixture);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(fixture,s).ReceiveAsync(s.Scope,other.Receipt,other.Fingerprint,default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(fixture,s).ReceiveAsync(s.Scope with { LocationIds = [] },s.Receipt,s.Fingerprint,default)); await Empty(s);
    }
    [ErpPostgresTheory]
    [InlineData("zero")] [InlineData("expiry")] [InlineData("held")] [InlineData("cancelled")] [InlineData("cost")]
    public async Task Invalid_or_unavailable_receipt_is_atomic(string kind)
    {
        var s = await Seed(fixture);
        await using(var db = fixture.Context())
        {
            var r = await db.StockReceipts.Include(x => x.Items).ThenInclude(x => x.Product).SingleAsync(x => x.Id == s.Receipt);
            if(kind=="zero") foreach(var l in r.Items) l.QuantityReceived=0;
            if(kind=="expiry") r.Items.First().ExpiryDate=null;
            if(kind=="held") { var u=User.Create(s.Inventory.Tenant,Guid.NewGuid()+"@test.local","Other","hash","store_manager"); db.Add(u); r.TakenByUserId=u.Id; r.Status="draft"; r.Source=ReceiptSource.AutoReplenishment; }
            if(kind=="cancelled") r.Status="cancelled";
            if(kind=="cost") r.Items.First().PricePurchase=9_999_999_999m;
            await db.SaveChangesAsync(); s=s with { Fingerprint=ReceiptPostingRules.Fingerprint(r) };
        }
        var e=await Record.ExceptionAsync(()=>Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default));
        Assert.True(e is ArgumentException or InventoryPostingConflictException); await Empty(s);
    }
    [ErpPostgresTheory]
    [InlineData("ledger")] [InlineData("notification")] [InlineData("marker")] [InlineData("movement")] [InlineData("projection")] [InlineData("metadata")]
    public async Task Deferred_fence_rolls_back_incomplete_graph(string fault)
    {
        var s=await Seed(fixture); var store=Store(fixture,s,true,new Sabotage(fault));
        var error=await Record.ExceptionAsync(()=>store.ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default));
        while(error?.InnerException!=null) error=error.InnerException;
        Assert.Equal("P4F01",Assert.IsType<PostgresException>(error).SqlState); await Empty(s);
        await using var db=fixture.Context(); Assert.Equal("ordered",(await db.StockReceipts.SingleAsync(x=>x.Id==s.Receipt)).Status);
    }
    private sealed class Sabotage(string fault) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            var db=(AppDbContext)data.Context!;
            if(!db.ChangeTracker.Entries<ShelfGuard.Domain.Entities.Erp.BusinessDocument>().Any(x=>x.Entity.Status=="posted")) return result;
            if(fault=="marker") await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.erp_posting_id','',true)",ct);
            if(fault=="notification") await db.Database.ExecuteSqlRawAsync("DELETE FROM public.notification_queue WHERE \"EventType\"='receipt.created'",ct);
            if(fault=="movement") await db.Database.ExecuteSqlRawAsync("DELETE FROM public.stock_movements WHERE \"MovementType\"='receipt'",ct);
            if(fault=="projection") await db.Database.ExecuteSqlRawAsync("DELETE FROM public.inventory_balance_projections WHERE \"BatchId\" IN (SELECT \"Id\" FROM public.product_stock WHERE \"SourceType\"='receipt')",ct);
            if(fault=="metadata") await db.Database.ExecuteSqlRawAsync("UPDATE public.product_stock SET \"BatchNumber\"='changed-before-commit' WHERE \"SourceType\"='receipt'",ct);
            // An extra stock row without a matching line must be rejected immediately by the INSERT guard.
            if(fault=="ledger") await db.Database.ExecuteSqlRawAsync("UPDATE public.product_stock SET \"Quantity\"=\"Quantity\"+1 WHERE \"SourceType\"='receipt'",ct);
            return result;
        }
    }
    [ErpPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_stale_legacy_cancel_or_line_save_rolls_back_after_ERP_receive(bool editLine)
    {
        var s=await Seed(fixture); await using var legacy=fixture.Context();
        var old=await legacy.StockReceipts.Include(x=>x.Items).SingleAsync(x=>x.Id==s.Receipt);
        await Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default);
        if(editLine) old.Items.First().QuantityReceived=99; else old.Status="cancelled";
        legacy.Entry(old).State=EntityState.Modified;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>legacy.SaveChangesAsync());
        await using var check=fixture.Context(); Assert.Equal("received",(await check.StockReceipts.SingleAsync(x=>x.Id==s.Receipt)).Status);
        Assert.False(await check.StockReceiptItems.AnyAsync(x=>x.ReceiptId==s.Receipt&&x.QuantityReceived==99));
    }
    private async Task Empty(Scenario s)
    {
        await using var db=fixture.Context(); Assert.False(await db.ProductStocks.AnyAsync(x=>x.SourceId==s.Receipt));
        Assert.False(await db.DocumentPostings.AnyAsync(x=>x.TenantId==s.Inventory.Tenant&&x.IdempotencyKey==ReceiptPostingRules.Key(s.Receipt)));
        Assert.False(await db.NotificationQueues.AnyAsync(x=>x.TenantId==s.Inventory.Tenant&&x.EventType=="receipt.created"));
    }
    [ErpPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Effective_receiving_denial_and_live_temporary_grant_are_rechecked(bool grant)
    {
        var s=await Seed(fixture);
        await using(var db=fixture.Context())
        {
            var user=await db.Users.SingleAsync(u=>u.Id==s.Inventory.Actor);user.SetPermissions(new(){{"receipts",false}});
            if(grant)db.Add(UserPermissionGrant.Create(s.Inventory.Tenant,user.Id,"receipts",DateTime.UtcNow.AddHours(1),user.Id));
            await db.SaveChangesAsync();
        }
        if(grant)Assert.False((await Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default)).Replayed);
        else {await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default));await Empty(s);}
    }
}
