using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class ReceiptCategoryWorkTests(ErpSchemaFixture fixture)
{
 private sealed class Access(WorkAccessScope s):IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct=default)=>Task.FromResult<WorkAccessScope?>(s); }
 private sealed class RuntimeRole(string role):DbConnectionInterceptor {
  public override async Task ConnectionOpenedAsync(DbConnection c,ConnectionEndEventData e,CancellationToken ct=default){await using var cmd=c.CreateCommand();cmd.CommandText="SET ROLE "+role;await cmd.ExecuteNonQueryAsync(ct);}
 }
 private ReceiptCategoryWorkStore Work(WorkAccessScope scope)=>new(new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(new RuntimeRole(fixture.ReaderRole)).IgnoreManyServiceProvidersWarning().Options,new Access(scope));
 private async Task<(ReceiptPostingPostgresTests.Scenario S,WorkAccessScope Other)> Seed(){
  var s=await ReceiptPostingPostgresTests.Seed(fixture,2);await using var db=fixture.Context();
  var alcohol=new PlatformCategory{Name="Алкоголь"};var milk=new PlatformCategory{Name="Молочка"};
  var original=await db.Items.SingleAsync(x=>x.Id==s.Inventory.Item);original.CategoryId=alcohol.Id;
  var item=new Item{TenantId=s.Inventory.Tenant,Name="Молоко",CategoryId=milk.Id};var otherItem=new Item{TenantId=s.Inventory.Tenant,Name="Без категорії"};
  db.AddRange(alcohol,milk,item,otherItem);
  db.Add(new StockReceiptItem{ReceiptId=s.Receipt,ProductId=item.Id,QuantityOrdered=3,PricePurchase=20,ExpiryDate=new(2027,1,1)});
  for(int i=0;i<2;i++)db.Add(new StockReceiptItem{ReceiptId=s.Receipt,ProductId=otherItem.Id,QuantityOrdered=1,ExpiryDate=new(2027,1,1)});
  var user=User.Create(s.Inventory.Tenant,Guid.NewGuid()+"@test.local","Другий менеджер","hash","storekeeper");db.AddRange(user,UserLocation.Create(s.Inventory.Tenant,user.Id,s.Inventory.Location,null));await db.SaveChangesAsync();return(s,s.Scope with{UserId=user.Id,Role="storekeeper"});
 }
 private async Task<ReceiptCategorySave> Save(Guid receipt,ReceiptCategoryGroupDto g,decimal qty=2,bool complete=true){await using var db=fixture.Context();var lines=await db.StockReceiptItems.Where(x=>x.ReceiptId==receipt&&g.LineIds.Contains(x.Id)).ToListAsync();return new(Guid.NewGuid(),g.Fingerprint,lines.Select(x=>new UpdateItemPayload(x.Id,qty,x.PricePurchase,x.ExpiryDate,x.BatchNumber,x.DiscrepancyNotes)).ToList(),complete);}
 [Fact] public void Missing_category_uses_other_and_membership_is_a_complete_partition(){
  var r=new StockReceipt{TenantId=Guid.NewGuid(),Status="ordered"};var cat=new PlatformCategory{Name="Молочка"};
  for(int i=0;i<201;i++){var p=new Item{TenantId=r.TenantId,Name="Item",Category=i%2==0?cat:null};r.Items.Add(new(){ReceiptId=r.Id,ProductId=p.Id,Product=p,QuantityOrdered=1,ExpiryDate=new(2027,1,1)});}
  var state=ReceiptCategoryWorkRules.Create(r);Assert.Equal(2,state.Groups.Length);Assert.Contains(state.Groups,g=>g.Key=="other"&&g.Name=="Інше");Assert.True(ReceiptCategoryWorkRules.Partition(r,state));Assert.False(ReceiptCategoryWorkRules.Ready(r,state));
 }

 [Fact] public void Completion_and_handoffs_have_independent_review_versions(){
  var r=new StockReceipt{TenantId=Guid.NewGuid(),Status="ordered"};
  foreach(var name in new[]{"Молочка","Алкоголь"}){var cat=new PlatformCategory{Name=name};var item=new Item{TenantId=r.TenantId,Name=name,Category=cat};r.Items.Add(new(){ReceiptId=r.Id,ProductId=item.Id,Product=item,QuantityOrdered=1,QuantityReceived=1,ExpiryDate=new(2027,1,1)});}
  var state=ReceiptCategoryWorkRules.Create(r);var a=state.Groups[0];var b=state.Groups[1];var hash=ReceiptCategoryWorkRules.Fingerprint(r,a);
  r.Items.Single(x=>b.LineIds.Contains(x.Id)).QuantityReceived=2;Assert.Equal(hash,ReceiptCategoryWorkRules.Fingerprint(r,a));
  Assert.NotEqual(hash,ReceiptCategoryWorkRules.Fingerprint(r,a with{Revision=a.Revision+1}));
  foreach(var g in state.Groups.Select((g,i)=>(g,i))){var owned=g.g with{OwnerId=Guid.NewGuid()};state.Groups[g.i]=owned with{CompletedFingerprint=ReceiptCategoryWorkRules.Fingerprint(r,owned)};}
  Assert.True(ReceiptCategoryWorkRules.Ready(r,state));ReceiptCategoryWorkRules.Write(r,state);ReceiptCategoryWorkRules.EnsureReady(r);
  r.Items.First().QuantityReceived=3;Assert.Throws<InventoryPostingConflictException>(()=>ReceiptCategoryWorkRules.EnsureReady(r));
 }
 [ErpPostgresFact] public async Task Different_managers_save_categories_concurrently_without_losing_each_others_lines_and_post_once(){
  var (s,other)=await Seed();var first=Work(s.Scope);var second=Work(other);var state=await first.ChangeAsync(s.Receipt,"start",null,null,default);Assert.Equal(3,state.Groups.Length);
  var a=state.Groups.Single(g=>g.Name=="Алкоголь");var b=state.Groups.Single(g=>g.Name=="Молочка");
  await first.ChangeAsync(s.Receipt,"take",a.Key,null,default);await second.ChangeAsync(s.Receipt,"take",b.Key,null,default);
  var stateA=await first.GetAsync(s.Receipt,default);var cmdA=await Save(s.Receipt,stateA.Groups.Single(g=>g.Key==a.Key),4);var cmdB=await Save(s.Receipt,stateA.Groups.Single(g=>g.Key==b.Key),5);
  await Task.WhenAll(first.ChangeAsync(s.Receipt,"save",a.Key,cmdA,default),second.ChangeAsync(s.Receipt,"save",b.Key,cmdB,default));
  var combined=await first.GetAsync(s.Receipt,default);Assert.True(combined.Groups.Single(g=>g.Key==a.Key).Ready);Assert.True(combined.Groups.Single(g=>g.Key==b.Key).Ready);
  var unassigned=combined.Groups.Single(g=>g.Key=="other");var taken=await first.ChangeAsync(s.Receipt,"take",unassigned.Key,null,default);await first.ChangeAsync(s.Receipt,"save",unassigned.Key,await Save(s.Receipt,taken.Groups.Single(g=>g.Key==unassigned.Key)),default);
  var preview=await ReceiptPostingPostgresTests.Store(fixture,s).PreviewAsync(s.Scope,s.Receipt,default);Assert.True(preview.CanReceive);
  var posted=await ReceiptPostingPostgresTests.Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,preview.Fingerprint,default);Assert.True((await ReceiptPostingPostgresTests.Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,preview.Fingerprint,default)).Replayed);
  await using var db=fixture.Context();Assert.Equal(5,await db.ProductStocks.CountAsync(x=>x.SourceId==s.Receipt));Assert.All(await db.StockReceiptItems.Where(x=>a.LineIds.Contains(x.Id)).ToListAsync(),x=>Assert.Equal(4m,x.QuantityReceived));Assert.All(await db.StockReceiptItems.Where(x=>b.LineIds.Contains(x.Id)).ToListAsync(),x=>Assert.Equal(5m,x.QuantityReceived));
 }
 [ErpPostgresFact] public async Task Same_category_has_one_owner_and_unfinished_categories_block_both_receiving_paths(){
  var (s,other)=await Seed();var one=Work(s.Scope);var two=Work(other);var state=await one.ChangeAsync(s.Receipt,"start",null,null,default);var key=state.Groups[0].Key;
  async Task<bool> Take(ReceiptCategoryWorkStore w){try{await w.ChangeAsync(s.Receipt,"take",key,null,default);return true;}catch(InventoryPostingConflictException){return false;}}
  Assert.Single(await Task.WhenAll(Take(one),Take(two)),x=>x);var context=await ReceiptPostingPostgresTests.Store(fixture,s).PreviewAsync(s.Scope,s.Receipt,default);Assert.False(context.CanReceive);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>ReceiptPostingPostgresTests.Store(fixture,s).ReceiveAsync(s.Scope,s.Receipt,context.Fingerprint,default));
  await using var db=fixture.Context();var legacy=new ReceiptService(new ReceiptRepository(db),NSubstitute.Substitute.For<ShelfGuard.Domain.Interfaces.IActivityLogRepository>());Assert.Equal(ReceiptErrors.CategoriesIncomplete,(await legacy.ReceiveAsync(s.Receipt,s.Scope.UserId)).Error);Assert.Equal(ReceiptErrors.CategoryWorkActive,(await legacy.UpdateItemsAsync(s.Receipt,new([],null),default,s.Scope.UserId)).Error);
 }
 [ErpPostgresFact] public async Task Other_bucket_can_be_split_between_managers_and_saves_cannot_escape_their_partition(){
  var (s,other)=await Seed();var one=Work(s.Scope);var two=Work(other);var state=await one.ChangeAsync(s.Receipt,"start",null,null,default);var bucket=state.Groups.Single(g=>g.Key=="other");
  var split=await one.ChangeAsync(s.Receipt,"take",bucket.Key,null,default,[bucket.LineIds[0]]);Assert.Equal(4,split.Groups.Length);var mine=split.Groups.Single(g=>g.OwnerId==s.Scope.UserId);Assert.Single(mine.LineIds);
  await two.ChangeAsync(s.Receipt,"take","other",null,default);var valid=await Save(s.Receipt,mine);
  await Assert.ThrowsAsync<ArgumentException>(()=>one.ChangeAsync(s.Receipt,"save",mine.Key,valid with{Items=[..valid.Items,valid.Items[0] with{ItemId=bucket.LineIds[1]}]},default));
  await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>two.ChangeAsync(s.Receipt,"save",mine.Key,valid,default));
  await one.ChangeAsync(s.Receipt,"save",mine.Key,valid,default);Assert.True((await one.ChangeAsync(s.Receipt,"save",mine.Key,valid,default)).Groups.Single(g=>g.Key==mine.Key).Ready);
  await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>one.ChangeAsync(s.Receipt,"save",mine.Key,valid with{Complete=false},default));
 }
 [ErpPostgresFact] public async Task Replay_stale_review_release_permissions_and_reassignment_are_enforced(){
  var (s,other)=await Seed();var one=Work(s.Scope);var two=Work(other);var state=await one.ChangeAsync(s.Receipt,"start",null,null,default);var g=state.Groups[0];var owned=await one.ChangeAsync(s.Receipt,"take",g.Key,null,default);g=owned.Groups.Single(x=>x.Key==g.Key);var cmd=await Save(s.Receipt,g);
  await Assert.ThrowsAsync<ArgumentException>(()=>one.ChangeAsync(s.Receipt,"save",g.Key,cmd with{Items=cmd.Items.Select(x=>x with{PricePurchase=null}).ToList()},default));
  await one.ChangeAsync(s.Receipt,"save",g.Key,cmd,default);await one.ChangeAsync(s.Receipt,"save",g.Key,cmd,default);
  await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>one.ChangeAsync(s.Receipt,"save",g.Key,cmd with{RequestId=Guid.NewGuid()},default));
  await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>two.ChangeAsync(s.Receipt,"reopen",g.Key,null,default));
  var reopened=await one.ChangeAsync(s.Receipt,"reopen",g.Key,null,default);Assert.False(reopened.Groups.Single(x=>x.Key==g.Key).Ready);Assert.NotEqual(g.Fingerprint,reopened.Groups.Single(x=>x.Key==g.Key).Fingerprint);
  await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>two.ChangeAsync(s.Receipt,"release",g.Key,null,default));await one.ChangeAsync(s.Receipt,"release",g.Key,null,default);var reassigned=await two.ChangeAsync(s.Receipt,"take",g.Key,null,default);Assert.False(reassigned.Groups.Single(x=>x.Key==g.Key).Ready);
 }
 [ErpPostgresFact] public async Task Fresh_scope_permissions_and_tenant_are_checked_for_every_read_and_write(){
  var (s,other)=await Seed();await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Work(s.Scope with{LocationIds=[]}).GetAsync(s.Receipt,default));var (alien,_)=await Seed();await Assert.ThrowsAsync<InventoryPostingNotFoundException>(()=>Work(alien.Scope).GetAsync(s.Receipt,default));
  await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==other.UserId)).SetPermissions(new(){["receipts"]=false});await db.SaveChangesAsync();}
  await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Work(other).ChangeAsync(s.Receipt,"start",null,null,default));
 }
}
