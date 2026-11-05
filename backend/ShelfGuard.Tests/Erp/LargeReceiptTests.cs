using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class LargeReceiptTests(ErpSchemaFixture fixture)
{
    private static StockReceipt RulesReceipt(int count)
    {
        var r = new StockReceipt { TenantId=Guid.NewGuid(),Status="ordered" };
        var item = new Item { TenantId=r.TenantId,Name="Large receipt item",Unit="шт" };
        for(var i=0;i<count;i++)r.Items.Add(new(){ReceiptId=r.Id,ProductId=item.Id,Product=item,QuantityOrdered=1.25m,PricePurchase=12.50m,ExpiryDate=new(2027,1,1)});
        return r;
    }
    [Theory] [InlineData(101)] [InlineData(1702)] [InlineData(2000)]
    public void Rules_accept_whole_large_receipt_and_fingerprint_includes_its_last_line(int count)
    {
        var r=RulesReceipt(count);ReceiptPostingRules.Validate(r,Guid.NewGuid());var hash=ReceiptPostingRules.Fingerprint(r);
        r.Items.Last().QuantityReceived=0.25m;Assert.NotEqual(hash,ReceiptPostingRules.Fingerprint(r));
    }
    [Fact] public void Over_limit_and_invalid_last_line_are_rejected()
    {
        Assert.Throws<ArgumentException>(()=>ReceiptPostingRules.Validate(RulesReceipt(2001),Guid.NewGuid()));
        var r=RulesReceipt(2000);r.Items.Last().QuantityReceived=0.001m;Assert.Throws<ArgumentException>(()=>ReceiptPostingRules.Validate(r,Guid.NewGuid()));
    }
    [Fact] public void Maximum_size_posting_envelope_round_trips_under_the_existing_one_MiB_consumer_limit()
    {
        var entries=Enumerable.Range(1,2000).Select(i=>new InventoryLedgerDeltaV1(Guid.NewGuid(),i,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),99999999.99m,99.99m,decimal.Round(99999999.99m*99.99m,4),"UAH")).ToArray();
        var envelope=ErpEventContracts.ForPosting(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,new(Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),entries));
        var raw=ErpEventContracts.Serialize(envelope);Assert.True(System.Text.Encoding.UTF8.GetByteCount(raw)<1048576);Assert.NotNull(ErpEventContracts.Deserialize(raw));
    }
    private async Task<ReceiptPostingPostgresTests.Scenario> Seed(int count)
    {
        var s=await ReceiptPostingPostgresTests.Seed(fixture);
        await using var db=fixture.Context();var r=await db.StockReceipts.Include(x=>x.Items).ThenInclude(x=>x.Product).SingleAsync(x=>x.Id==s.Receipt);
        var item=r.Items.First().Product!;
        for(var i=r.Items.Count;i<count;i++){var line=new StockReceiptItem{ReceiptId=r.Id,ProductId=s.Inventory.Item,Product=item,QuantityOrdered=1.25m,QuantityReceived=i==count-1?0.25m:null,PricePurchase=i%2==0?12.50m:null,ExpiryDate=new(2027,1,1),BatchNumber="large-"+i};db.Add(line);}
        await db.SaveChangesAsync();db.ChangeTracker.Clear();var saved=await db.StockReceipts.AsNoTracking().Include(x=>x.Items).ThenInclude(x=>x.Product).SingleAsync(x=>x.Id==s.Receipt);return s with {Fingerprint=ReceiptPostingRules.Fingerprint(saved)};
    }
    [ErpPostgresFact] public async Task Two_thousand_lines_commit_one_atomic_graph_and_replay_without_duplicate_stock()
    {
        var s=await Seed(2000);var store=ReceiptPostingPostgresTests.Store(fixture,s);
        Assert.Equal(2000,(await store.PreviewAsync(s.Scope,s.Receipt,default)).MaxReceiptLines);
        var result=await store.ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default);Assert.True((await store.ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default)).Replayed);
        await using var db=fixture.Context();Assert.Equal(2000,await db.ProductStocks.CountAsync(x=>x.SourceId==s.Receipt));
        Assert.Equal(2000,await db.InventoryLedgerEntries.CountAsync(x=>x.PostingId==result.PostingId));Assert.Equal(2000,await db.DocumentLines.CountAsync(x=>x.DocumentId==result.DocumentId));
        Assert.Equal(2000,await db.InventoryBalanceProjections.CountAsync(x=>x.TenantId==s.Inventory.Tenant&&x.BatchId!=s.Inventory.Batch));
        Assert.Single(await db.OutboxEvents.Where(x=>x.PostingId==result.PostingId).ToListAsync());
        var evt=await db.OutboxEvents.SingleAsync(x=>x.PostingId==result.PostingId);Assert.NotNull(ErpEventContracts.Deserialize(ErpEventContracts.Serialize(ErpEventContracts.FromOutbox(evt))));
        Assert.Equal("received",(await db.StockReceipts.SingleAsync(x=>x.Id==s.Receipt)).Status);
    }
    [ErpPostgresFact] public async Task Summaries_count_large_lines_without_serializing_them_and_enforce_tenant_store_filters()
    {
        var s=await Seed(1702);var store=ReceiptPostingPostgresTests.Store(fixture,s);var page=await store.ListAsync(s.Scope,s.Inventory.Location,1,"","ordered",default);
        Assert.Equal(1702,Assert.Single(page.Items).ItemCount);Assert.True(JsonSerializer.Serialize(page).Length<1500);
        Assert.Single((await store.ListAsync(s.Scope,s.Inventory.Location,1,s.Receipt.ToString(),"",default)).Items);
        Assert.Empty((await store.ListAsync(s.Scope,s.Inventory.Location,1,"","received",default)).Items);
        Assert.Empty((await store.ListAsync(s.Scope,s.Inventory.Location,2,"","",default)).Items);
        await Assert.ThrowsAsync<ShelfGuard.Application.Services.WorkAccessDeniedException>(()=>store.ListAsync(s.Scope with {LocationIds=[]},s.Inventory.Location,1,"","",default));
        var other=await ReceiptPostingPostgresTests.Seed(fixture);Assert.Empty((await store.ListAsync(other.Scope,other.Inventory.Location,1,s.Receipt.ToString(),"",default)).Items);
    }
    [ErpPostgresFact] public async Task Missing_final_movement_rolls_back_all_seventeen_hundred_lines()
    {
        var s=await Seed(1702);var failure=await Record.ExceptionAsync(()=>ReceiptPostingPostgresTests.Store(fixture,s,true,new OmitMovement(s.Receipt)).ReceiveAsync(s.Scope,s.Receipt,s.Fingerprint,default));
        while(failure?.InnerException!=null)failure=failure.InnerException;Assert.Equal("P4F01",Assert.IsType<Npgsql.PostgresException>(failure).SqlState);
        await using var db=fixture.Context();Assert.False(await db.ProductStocks.AnyAsync(x=>x.SourceId==s.Receipt));Assert.False(await db.DocumentPostings.AnyAsync(x=>x.TenantId==s.Inventory.Tenant&&x.IdempotencyKey==ReceiptPostingRules.Key(s.Receipt)));Assert.Equal("ordered",(await db.StockReceipts.SingleAsync(x=>x.Id==s.Receipt)).Status);
    }
    private sealed class OmitMovement(Guid receipt):SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            var db=(AppDbContext)data.Context!;if(db.ChangeTracker.Entries<BusinessDocument>().Any(x=>x.Entity.Status=="posted"))
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_movements WHERE \"Id\" IN (SELECT \"Id\" FROM stock_movements WHERE \"ReferenceId\"={receipt} ORDER BY \"Id\" DESC LIMIT 1)",ct);
            return result;
        }
    }
}
