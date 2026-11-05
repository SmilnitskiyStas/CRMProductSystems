using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class PosInventoryPostingPostgresTests(ErpSchemaFixture fixture)
{
    internal sealed record Scenario(WriteOffPostingPostgresTests.Scenario Source, Guid Shift, string Barcode)
    {
        internal InventoryFenceScenario S => Source.Inventory;
        internal WorkAccessScope Scope => Source.Scope;
        internal CreateSaleRequest Request(decimal qty=1,Guid? client=null) => new(Shift,[new(Barcode,qty)],"Cash",500m,ClientTransactionId:client??Guid.NewGuid());
    }
    internal static async Task<Scenario> Seed(ErpSchemaFixture f,bool multi=false,bool fenced=true)
    {
        var s=await WriteOffPostingPostgresTests.Seed(f,multi,fenced);var barcode="pos-"+Guid.NewGuid().ToString("N");
        var shift=new PosShift{TenantId=s.Inventory.Tenant,StoreId=s.Inventory.Location,CashierId=s.Inventory.Actor,ShiftNumber=1};
        await using var db=f.Context();(await db.Tenants.SingleAsync(t=>t.Id==s.Inventory.Tenant)).UpdateModules(["inventory","pos"]);
        var item=await db.Items.SingleAsync(i=>i.Id==s.Inventory.Item);item.Barcodes=[barcode];item.PriceRetail=50;item.PricePurchase=999;item.VatRate=20;
        db.Add(shift);await db.SaveChangesAsync();return new(s,shift.Id,barcode);
    }
    internal static PosService Service(AppDbContext db,Scenario s,bool enabled=true,WorkAccessScope? scope=null,ShelfGuard.Domain.Interfaces.IStockRepository? stock=null,IPosPriceSnapshots? prices=null,ShelfGuard.Application.Features.Pricing.IPosBasePriceResolver? basePrices=null,IPosScopedPrices? scopedPrices=null,IPosPromotionSnapshots? promotionProofs=null)
    {
        var access=Substitute.For<IWorkAccessContext>();access.GetAsync(Arg.Any<CancellationToken>()).Returns(scope??s.Scope);
        var bridge=new PosInventoryPostingBridge(db,s.Source.Gate,new(enabled),access);
        var factory=Substitute.For<IFiscalServiceFactory>();factory.GetForTenantAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult<IFiscalService>(new NoopFiscalService()));
        return new(new PosRepository(db,bridge),stock??new StockRepository(db),new ItemRepository(db),new DiscountRepository(db),factory,new LoyaltyRepository(db),new CustomerRepository(db),NullLogger<PosService>.Instance,registers:new CashRegisterRepository(db),priceSnapshots:prices,basePrices:basePrices,scopedPrices:scopedPrices,promotionSnapshots:promotionProofs);
    }
    private AppDbContext Context(params IInterceptor[] interceptors)
    {
        var options=new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).IgnoreManyServiceProvidersWarning();
        if(interceptors.Length>0)options.AddInterceptors(interceptors);return new(options.Options);
    }
    [ErpPostgresFact]
    public async Task Real_sale_commits_POS_ERP_tax_events_stock_and_shift_and_retry_does_not_repost()
    {
        var s=await Seed(fixture);var request=s.Request();await using var db=Context();var svc=Service(db,s);
        var first=await svc.CreateSaleAsync(s.S.Tenant,s.S.Actor,request);Assert.Null(first.Error);Assert.NotNull(first.Sale);
        var replay=await svc.CreateSaleAsync(s.S.Tenant,s.S.Actor,request);Assert.Null(replay.Error);Assert.Equal(first.Sale.TransactionId,replay.Sale!.TransactionId);
        await using var check=fixture.Context();var doc=await check.BusinessDocuments.SingleAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosInventoryPostingBridge.Source);
        Assert.Equal("posted",doc.Status);var entry=await check.InventoryLedgerEntries.SingleAsync(e=>e.DocumentId==doc.Id);Assert.Equal(-1,entry.QuantityDelta);Assert.Null(entry.UnitCost);Assert.Null(entry.ValueDelta);
        Assert.Equal(2.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);
        Assert.Equal(50,(await check.PosShifts.SingleAsync(x=>x.Id==s.Shift)).TotalSales);
        var sale=await check.PosTransactions.Include(t=>t.Items).SingleAsync(t=>t.Id==first.Sale.TransactionId);Assert.Equal(8.33m,sale.TaxAmount);Assert.Equal(50,sale.Items.Sum(i=>i.TaxableAmount));
        Assert.Equal(1,await check.OutboxEvents.CountAsync(o=>o.TenantId==s.S.Tenant&&o.DeliveryScope=="live"));
        var line=await check.DocumentLines.SingleAsync(l=>l.DocumentId==doc.Id);using var meta=JsonDocument.Parse(line.Metadata!);
        Assert.Equal(sale.Id.ToString(),meta.RootElement.GetProperty("source").GetProperty("header").GetProperty("Id").GetString());
    }
    [ErpPostgresFact]
    public async Task A_multi_batch_sale_has_exact_ledger_deltas_even_though_receipt_points_at_first_batch()
    {
        var s=await Seed(fixture,true);await using var db=Context();var result=await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request(4.5m));Assert.Null(result.Error);
        await using var check=fixture.Context();var entries=await check.InventoryLedgerEntries.Where(e=>e.TenantId==s.S.Tenant&&e.QuantityDelta<0).ToListAsync();
        Assert.Equal(2,entries.Count);Assert.Equal(-3.25m,entries.Single(e=>e.BatchId==s.S.Batch).QuantityDelta);Assert.Equal(-1.25m,entries.Single(e=>e.BatchId==s.Source.SecondBatch).QuantityDelta);
        Assert.Single(await check.PosTransactionItems.Where(i=>i.TransactionId==result.Sale!.TransactionId).ToListAsync());
    }
    [ErpPostgresTheory] [InlineData("outbox")] [InlineData("projection")] [InlineData("movement")] [InlineData("marker")]
    public async Task Deferred_fault_rolls_back_sale_stock_and_shift_and_clears_tracker(string fault)
    {
        var s=await Seed(fixture);await using var db=Context(new Fault(fault));
        if(fault=="outbox")Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request()))).SqlState);
        else Assert.Equal(409,(await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).StatusCode);
        Assert.Empty(db.ChangeTracker.Entries());await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
        Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);Assert.Equal(0,(await check.PosShifts.SingleAsync(x=>x.Id==s.Shift)).TotalSales);
        Assert.False(await check.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosInventoryPostingBridge.Source));
    }
    [ErpPostgresTheory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task Enabled_writer_and_database_fence_are_both_required(bool enabled,bool fenced)
    {
        var s=await Seed(fixture,fenced:fenced);await using var db=Context();var result=await Service(db,s,enabled).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request());Assert.Equal(409,result.StatusCode);
        await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
    }
    [ErpPostgresFact]
    public async Task Unfenced_default_off_sales_keep_the_legacy_path_without_ERP_document()
    {
        var s=await Seed(fixture,fenced:false);await using var db=Context();var result=await Service(db,s,false).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request());Assert.Null(result.Error);
        await using var check=fixture.Context();Assert.False(await check.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosInventoryPostingBridge.Source));
    }
    [ErpPostgresTheory] [InlineData("module")] [InlineData("permission")] [InlineData("assignment")] [InlineData("inactive")]
    public async Task Live_POS_rights_are_checked_inside_the_posting_transaction(string kind)
    {
        var s=await Seed(fixture);await using(var change=fixture.Context())
        {
            if(kind=="module")(await change.Tenants.SingleAsync(t=>t.Id==s.S.Tenant)).UpdateModules(["inventory"]);
            if(kind=="permission")(await change.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"pos",false}});
            if(kind=="assignment")await change.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_locations WHERE \"TenantId\"={s.S.Tenant}");
            if(kind=="inactive")(await change.Locations.SingleAsync(l=>l.Id==s.S.Location)).IsActive=false;
            await change.SaveChangesAsync();
        }
        await using var db=Context();var result=await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request());Assert.Equal(409,result.StatusCode);
    }
    [ErpPostgresTheory] [InlineData("header")] [InlineData("line")] [InlineData("event")] [InlineData("truncate")]
    public async Task Committed_POS_source_financial_lines_and_events_cannot_be_rewritten(string target)
    {
        var s=await Seed(fixture);await using(var db=Context())Assert.Null((await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).Error);
        await using var change=fixture.Context();var error=await Assert.ThrowsAsync<PostgresException>(async()=>{
            if(target=="header")await change.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos_transactions SET \"TotalAmount\"=1 WHERE \"TenantId\"={s.S.Tenant}");
            if(target=="line")await change.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transaction_items WHERE \"TransactionId\" IN (SELECT \"Id\" FROM pos_transactions WHERE \"TenantId\"={s.S.Tenant})");
            if(target=="event")await change.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_events WHERE \"TenantId\"={s.S.Tenant}");
            if(target=="truncate")await change.Database.ExecuteSqlRawAsync("TRUNCATE stock_events");
        });Assert.Equal("P4F01",error.SqlState);
    }
    [ErpPostgresFact]
    public async Task Fiscal_retry_after_a_later_sale_updates_fiscal_metadata_without_old_stock_checks()
    {
        var s=await Seed(fixture);Guid first;
        await using(var db=Context()){var svc=Service(db,s);var a=await svc.CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request());Assert.Null(a.Error);first=a.Sale!.TransactionId;Assert.Null((await svc.CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).Error);}
        await using(var change=Context()){var t=await change.PosTransactions.SingleAsync(t=>t.Id==first);t.FiscalNumber="synthetic-fiscal";t.Status="fiscalized";t.RetryCount++;await new PosRepository(change).SaveChangesAsync();}
        await using var check=fixture.Context();Assert.Equal(1.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);Assert.Equal(2,await check.OutboxEvents.CountAsync(o=>o.TenantId==s.S.Tenant&&o.DeliveryScope=="live"));
    }
    [ErpPostgresFact]
    public async Task Repeatable_read_receipt_edits_are_still_rejected_by_source_guards()
    {
        // The new sale is inserted during posting. A reader seeing it afterwards must still obey immutable guards.
        var s=await Seed(fixture);await using(var db=Context())Assert.Null((await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).Error);
        await using var old=fixture.Context();await using var tx=await old.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var item=await old.PosTransactionItems.Where(i=>i.Transaction!.TenantId==s.S.Tenant).FirstAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>old.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos_transaction_items SET \"PriceFinal\"=1 WHERE \"Id\"={item.Id}"));Assert.Equal("P4F01",error.SqlState);await tx.RollbackAsync();
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_sales_never_oversell_and_identical_client_keys_return_the_same_sale(bool sameKey)
    {
        var s=await Seed(fixture);await using var a=Context();await using var b=Context();
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var arrivals=0;
        ShelfGuard.Domain.Interfaces.IStockRepository Stock(AppDbContext db)
        {
            var inner=new StockRepository(db);var wrapped=Substitute.For<ShelfGuard.Domain.Interfaces.IStockRepository>();
            wrapped.GetFefoOrderedAsync(Arg.Any<Guid>(),Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(async call=>{
                var result=await inner.GetFefoOrderedAsync(call.ArgAt<Guid>(0),call.ArgAt<Guid>(1),call.ArgAt<CancellationToken>(2));
                if(Interlocked.Increment(ref arrivals)==2)ready.TrySetResult();await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));return result;});
            wrapped.GetAllAsync(Arg.Any<Guid?>(),Arg.Any<string?>(),Arg.Any<Guid?>(),Arg.Any<Guid?>(),Arg.Any<CancellationToken>()).Returns(call=>inner.GetAllAsync(call.ArgAt<Guid?>(0),call.ArgAt<string?>(1),call.ArgAt<Guid?>(2),call.ArgAt<Guid?>(3),call.ArgAt<CancellationToken>(4)));
            wrapped.When(x=>x.Update(Arg.Any<ProductStock>())).Do(call=>inner.Update(call.Arg<ProductStock>()));return wrapped;
        }
        var request=s.Request(2.25m);var other=sameKey?request:s.Request(2.25m);
        var results=await Task.WhenAll(Service(a,s,stock:Stock(a)).CreateSaleAsync(s.S.Tenant,s.S.Actor,request),Service(b,s,stock:Stock(b)).CreateSaleAsync(s.S.Tenant,s.S.Actor,other));
        if(sameKey){Assert.All(results,r=>Assert.Null(r.Error));Assert.Equal(results[0].Sale!.TransactionId,results[1].Sale!.TransactionId);}
        else{Assert.Single(results,r=>r.Error==null);Assert.Single(results,r=>r.StatusCode==409);}
        await using var check=fixture.Context();Assert.Equal(1m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);Assert.Equal(1,await check.PosTransactions.CountAsync(t=>t.TenantId==s.S.Tenant));
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Changed_retry_is_a_conflict_and_does_not_create_another_ERP_posting(bool payment)
    {
        var s=await Seed(fixture);var request=s.Request();await using var db=Context();var service=Service(db,s);Assert.Null((await service.CreateSaleAsync(s.S.Tenant,s.S.Actor,request)).Error);
        var changed=payment?request with{PaymentAmount=499}:request with{Items=[new(s.Barcode,2)]};Assert.Equal(409,(await service.CreateSaleAsync(s.S.Tenant,s.S.Actor,changed)).StatusCode);
        await using var check=fixture.Context();Assert.Equal(1,await check.OutboxEvents.CountAsync(e=>e.TenantId==s.S.Tenant&&e.DeliveryScope=="live"));
    }
    [ErpPostgresTheory] [InlineData(1.001)] [InlineData(100000000)]
    public async Task Quantity_precision_and_overflow_are_rejected_before_stock_changes(decimal qty)
    {
        var s=await Seed(fixture);await using var db=Context();Assert.Equal(400,(await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request(qty))).StatusCode);
        await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
    }
    [ErpPostgresFact]
    public async Task An_ERP_sale_requires_a_persistent_client_identity_for_unknown_outcome_retry()
    {
        var s=await Seed(fixture);await using var db=Context();Assert.Equal(409,(await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request() with{ClientTransactionId=null})).StatusCode);
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [ErpPostgresFact]
    public async Task Live_temporary_permission_grant_can_override_POS_denial()
    {
        var s=await Seed(fixture);await using(var change=fixture.Context())
        {(await change.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"pos",false}});change.Add(UserPermissionGrant.Create(s.S.Tenant,s.S.Actor,"pos",DateTime.UtcNow.AddHours(1),s.S.Actor));await change.SaveChangesAsync();}
        await using var db=Context();Assert.Null((await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).Error);
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Loyalty_paid_amount_and_bonus_balance_commit_or_roll_back_with_the_ERP_sale(bool fail)
    {
        var s=await Seed(fixture);var consumer=new ConsumerAccount{Phone="+380"+Guid.NewGuid().ToString("N")[..9],PasswordHash="hash",FullName="Synthetic buyer"};
        var membership=new LoyaltyMembership{TenantId=s.S.Tenant,ConsumerAccountId=consumer.Id,TotpSecret="SYNTHETIC",Balance=100};
        await using(var seed=fixture.Context()){seed.AddRange(consumer,membership,new LoyaltyProgramSettings{TenantId=s.S.Tenant,AccrualRatePercent=3,RedemptionCapPercent=100});await seed.SaveChangesAsync();}
        await using(var db=fail?Context(new Fault("movement")):Context())
        {
            var result=await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request() with{LoyaltyMembershipId=membership.Id,RedeemAmount=10});
            if(fail)Assert.Equal(409,result.StatusCode);else Assert.Null(result.Error);
        }
        await using var check=fixture.Context();Assert.Equal(fail?100m:91.20m,(await check.LoyaltyMemberships.SingleAsync(m=>m.Id==membership.Id)).Balance);
        Assert.Equal(fail?0:2,await check.LoyaltyLedgerEntries.CountAsync(e=>e.TenantId==s.S.Tenant));
        if(!fail){var sale=await check.PosTransactions.Include(t=>t.Items).SingleAsync(t=>t.TenantId==s.S.Tenant);Assert.Equal(40,sale.TotalAmount);Assert.Equal(50,sale.Items.Sum(i=>i.LineTotal));Assert.Equal(40,sale.Items.Sum(i=>i.TaxableAmount));}
    }
    [ErpPostgresFact]
    public async Task Active_reservation_blocks_POS_without_consuming_the_reserved_item()
    {
        var s=await Seed(fixture);await using(var seed=fixture.Context())
        {
            var d=new BusinessDocument{TenantId=s.S.Tenant,DocumentType="reservation_source"};seed.Add(d);await seed.SaveChangesAsync();
            var line=new DocumentLine{TenantId=s.S.Tenant,DocumentId=d.Id,LineNumber=1,ItemId=s.S.Item,FromLocationId=s.S.Location,Quantity=1,ItemNameSnapshot="Reserved"};seed.Add(line);await seed.SaveChangesAsync();
            seed.Add(new InventoryReservation{TenantId=s.S.Tenant,DocumentId=d.Id,DocumentLineId=line.Id,ItemId=s.S.Item,LocationId=s.S.Location,Quantity=1,RemainingQuantity=1,IdempotencyKey=Guid.NewGuid().ToString("N")});await seed.SaveChangesAsync();
        }
        await using var db=Context();Assert.Equal(409,(await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).StatusCode);
        await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Verified_offline_receipt_keeps_original_price_and_VAT_and_pre_activation_queue_is_blocked(bool beforeActivation)
    {
        var s=await Seed(fixture);var at=DateTime.UtcNow.AddMinutes(beforeActivation?-60:0);
        var register=new CashRegister{TenantId=s.S.Tenant,StoreId=s.S.Location,Number=1,Name="Synthetic offline till"};register.IssueActivationCode("synthetic-code",DateTime.UtcNow.AddHours(1));
        await using(var seed=fixture.Context())
        {
            seed.Add(register);await seed.SaveChangesAsync();Assert.True(await new CashRegisterRepository(seed).TryActivateAsync(register.Id,"synthetic-code",new string('a',64),DateTime.UtcNow));
            await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos_shifts SET \"RegisterId\"={register.Id} WHERE \"Id\"={s.Shift}");
            seed.Add(new RegisterSession{TenantId=s.S.Tenant,RegisterId=register.Id,CashierId=s.S.Actor,StartedAt=at.AddMinutes(-2)});await seed.SaveChangesAsync();
            var item=await seed.Items.SingleAsync(i=>i.Id==s.S.Item);item.PriceRetail=80;item.VatRate=10;await seed.SaveChangesAsync();
        }
        var prices=new PosPriceSnapshots(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Pos:PriceSnapshotSecret","SYNTHETIC-W4H-SNAPSHOT-KEY-00000000000000000000"}}).Build());
        var signed=prices.Issue(new(s.S.Item,"Original item",[s.Barcode],50,20,"l",true,at.AddMinutes(-1),TenantId:s.S.Tenant,PricePurchase:12.5m),at.AddMinutes(-1));
        var request=s.Request() with{Items=[new(s.Barcode,1,signed.Token)],OccurredAt=at,ExpectedTotal=50};
        await using(var db=Context())
        {
            var svc=Service(db,s,prices:prices);var result=await svc.CreateSalesBatchAsync(s.S.Tenant,s.S.Actor,new([request]),registerId:register.Id);
            Assert.Null(result.Error);Assert.Equal(beforeActivation?"failed":"created",result.Result!.Results.Single().Status);
            if(!beforeActivation){var replay=await svc.CreateSalesBatchAsync(s.S.Tenant,s.S.Actor,new([request]),registerId:register.Id);Assert.Equal("duplicate",replay.Result!.Results.Single().Status);}
        }
        await using var check=fixture.Context();
        if(beforeActivation){Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);}
        else{var sale=await check.PosTransactions.Include(t=>t.Items).SingleAsync(t=>t.TenantId==s.S.Tenant);Assert.Equal(50,sale.TotalAmount);Assert.Equal(20,sale.Items.Single().VatRate);Assert.Equal(8.33m,sale.TaxAmount);var entry=await check.InventoryLedgerEntries.SingleAsync(e=>e.TenantId==s.S.Tenant&&e.QuantityDelta<0);Assert.InRange(Math.Abs((entry.OccurredAt-at).Ticks),0,10);}
    }
    [ErpPostgresFact]
    public async Task Legacy_pending_fiscal_receipt_can_finish_after_scope_activation_without_inventory_reposting()
    {
        var s=await Seed(fixture,fenced:false);var old=new PosTransaction{TenantId=s.S.Tenant,StoreId=s.S.Location,CashierId=s.S.Actor,ShiftId=s.Shift,ReceiptNumber="legacy-pending",TotalAmount=10};
        await using(var seed=fixture.Context()){seed.Add(old);await seed.SaveChangesAsync();await InventoryFenceTestHelper.Activate(seed,s.S.Tenant,s.S.Location,s.S.Actor);}
        await using(var db=Context()){var t=await db.PosTransactions.SingleAsync(t=>t.Id==old.Id);t.FiscalNumber="legacy-completed";t.Status="fiscalized";await new PosRepository(db).SaveChangesAsync();}
        await using var check=fixture.Context();Assert.False(await check.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosInventoryPostingBridge.Source));Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Inventory_cost_comes_from_the_received_batch_ledger_and_never_from_current_catalog_cost()
    {
        var s=await Seed(fixture);Guid id;string fingerprint;
        await using(var seed=fixture.Context())
        {
            (await seed.Tenants.SingleAsync(t=>t.Id==s.S.Tenant)).UpdateModules(["inventory","pos","procurement"]);
            var receipt=new StockReceipt{TenantId=s.S.Tenant,DestinationStoreId=s.S.Location,Status="ordered",CreatedBy=s.S.Actor};
            receipt.Items.Add(new(){ReceiptId=receipt.Id,ProductId=s.S.Item,QuantityOrdered=2,PricePurchase=12.5m,ExpiryDate=new(2027,1,1),BatchNumber="priced-pos-lot"});seed.Add(receipt);await seed.SaveChangesAsync();id=receipt.Id;
            fingerprint=ReceiptPostingRules.Fingerprint(await seed.StockReceipts.AsNoTracking().Include(r=>r.Items).ThenInclude(i=>i.Product).SingleAsync(r=>r.Id==id));
        }
        await ReceiptPostingPostgresTests.Store(fixture,new(s.S,id,fingerprint)).ReceiveAsync(s.Scope,id,fingerprint,default);
        await using(var db=Context())Assert.Null((await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request())).Error);
        await using var check=fixture.Context();var entry=await check.InventoryLedgerEntries.SingleAsync(e=>e.TenantId==s.S.Tenant&&e.QuantityDelta<0);Assert.Equal(12.5m,entry.UnitCost);Assert.Equal(-12.5m,entry.ValueDelta);Assert.Equal("priced-pos-lot",entry.BatchNumberSnapshot);
    }
    [ErpPostgresFact]
    public async Task Stock_history_counts_a_multi_batch_ERP_sale_once_despite_compatible_movements()
    {
        var s=await Seed(fixture,true);await using(var db=Context())Assert.Null((await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request(4.5m))).Error);
        await using var check=fixture.Context();var today=DateOnly.FromDateTime(DateTime.UtcNow);
        var history=await new MovementRepository(check).GetStockHistoryAsync(s.S.Tenant,s.S.Item,s.S.Location,today);
        Assert.Equal(2.75m,history.CurrentStock);Assert.Equal(2.75m,history.DailyChanges[today]); // Opening 7.25 minus one sale 4.50.
        Assert.Equal(2,await check.StockMovements.CountAsync(m=>m.TenantId==s.S.Tenant&&m.ReferenceType=="pos_sale"));
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Repeated_receipt_rows_cannot_request_more_than_the_combined_item_stock_on_either_path(bool fenced)
    {
        var s=await Seed(fixture,fenced:fenced);await using var db=Context();
        var request=s.Request() with{Items=[new(s.Barcode,2),new(s.Barcode,2)]};
        Assert.Equal(400,(await Service(db,s,fenced).CreateSaleAsync(s.S.Tenant,s.S.Actor,request)).StatusCode);
        await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresTheory] [InlineData(-.01)] [InlineData(500.001)]
    public async Task Tendered_money_cannot_be_negative_or_silently_rounded_before_persistent_retry(decimal amount)
    {
        var s=await Seed(fixture);await using var db=Context();Assert.Equal(400,(await Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request() with{PaymentAmount=amount})).StatusCode);
        await using var check=fixture.Context();Assert.False(await check.PosTransactions.AnyAsync(t=>t.TenantId==s.S.Tenant));
    }
    private sealed class Fault(string kind):DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken ct=default)
        {
            var db=(AppDbContext)data.Context!;if(!await db.BusinessDocuments.AnyAsync(d=>d.SourceSystem==PosInventoryPostingBridge.Source&&d.Status=="posted",ct))return result;
            if(kind=="outbox")await db.Database.ExecuteSqlRawAsync("DELETE FROM outbox_events WHERE \"PostingId\"::text=current_setting('app.erp_posting_id')",ct);
            if(kind=="projection")await db.Database.ExecuteSqlRawAsync("UPDATE inventory_balance_projections SET \"Quantity\"=\"Quantity\"+1 WHERE \"LastLedgerEntryId\" IN (SELECT \"Id\" FROM inventory_ledger_entries WHERE \"PostingId\"::text=current_setting('app.erp_posting_id'))",ct);
            if(kind=="movement")await db.Database.ExecuteSqlRawAsync("DELETE FROM stock_movements WHERE \"ReferenceId\"::text=(SELECT d.\"SourceId\" FROM business_documents d JOIN document_postings p ON p.\"DocumentId\"=d.\"Id\" WHERE p.\"Id\"::text=current_setting('app.erp_posting_id'))",ct);
            if(kind=="marker")await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.erp_posting_id','',true)",ct);
            return result;
        }
    }
}
