using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ReceiptSourceConcurrencyPostgresTests(ErpSchemaFixture fixture)
{
    private async Task<Guid> Seed()
    {
        await using var db=fixture.Context();
        var tenant=Tenant.Create("Synthetic receipt concurrency","receipt-race-"+Guid.NewGuid().ToString("N"));
        var location=new Location{TenantId=tenant.Id,Name="Synthetic warehouse"};
        var item=new Item{TenantId=tenant.Id,Name="Synthetic receipt item",Unit="pcs"};
        var receipt=new StockReceipt{TenantId=tenant.Id,DestinationStoreId=location.Id,Status="draft"};
        receipt.Items.Add(new(){ReceiptId=receipt.Id,ProductId=item.Id,QuantityOrdered=3,ExpiryDate=new(2027,1,1)});
        receipt.Items.Add(new(){ReceiptId=receipt.Id,ProductId=item.Id,QuantityOrdered=2,ExpiryDate=new(2027,1,1)});
        db.AddRange(tenant,location,item,receipt);await db.SaveChangesAsync();return receipt.Id;
    }
    private static async Task Change(ReceiptRepository repo,StockReceipt receipt,string operation)
    {
        if(operation=="add")await repo.AddItemAsync(new(){ReceiptId=receipt.Id,ProductId=receipt.Items.First().ProductId,QuantityOrdered=1,ExpiryDate=new(2027,1,1)});
        else repo.RemoveItems([receipt.Items.Last()]);
    }
    [ErpPostgresTheory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Child_edit_invalidates_an_already_loaded_review_and_rolls_back_its_counts(string operation)
    {
        var id=await Seed();await using var stale=fixture.Context();var oldRepo=new ReceiptRepository(stale);var old=(await oldRepo.GetByIdAsync(id))!;var first=old.Items.First();
        await using(var current=fixture.Context())
        {
            var repo=new ReceiptRepository(current);var row=(await repo.GetByIdAsync(id))!;
            // Keep the original first row in both contexts, irrespective of EF collection order.
            if(operation=="remove")repo.RemoveItems([row.Items.Single(x=>x.Id!=first.Id)]);else await Change(repo,row,operation);
            await repo.SaveChangesAsync();
        }
        first.QuantityReceived=9;oldRepo.UpdateItem(first);oldRepo.Update(old);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>oldRepo.SaveChangesAsync());
        await using var verify=fixture.Context();var saved=await verify.StockReceiptItems.Where(x=>x.ReceiptId==id).ToListAsync();
        Assert.Equal(operation=="add"?3:1,saved.Count);Assert.All(saved,x=>Assert.Null(x.QuantityReceived));
    }
    [ErpPostgresTheory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task A_child_writer_loaded_before_receiving_cannot_mutate_the_closed_receipt(string operation)
    {
        var id=await Seed();await using var stale=fixture.Context();var repo=new ReceiptRepository(stale);var row=(await repo.GetByIdAsync(id))!;
        await using(var current=fixture.Context()){var receipt=await current.StockReceipts.SingleAsync(x=>x.Id==id);receipt.Status="received";await current.SaveChangesAsync();}
        await Change(repo,row,operation);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>repo.SaveChangesAsync());
        await using var verify=fixture.Context();Assert.Equal("received",(await verify.StockReceipts.SingleAsync(x=>x.Id==id)).Status);Assert.Equal(2,await verify.StockReceiptItems.CountAsync(x=>x.ReceiptId==id));
    }
}
