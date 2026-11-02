using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class IntegratedInventoryPostgresTests(ErpSchemaFixture fixture)
{
    private DbContextOptions<AppDbContext> Options=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).IgnoreManyServiceProvidersWarning().Options;
    private async Task Hold(Guid batch){await using var db=fixture.Context();await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.product_stock SET \"Status\"='temp_violation' WHERE \"Id\"={batch}");}
    private async Task AssertReconciled(Guid tenant)
    {
        await using var db=fixture.Context();
        var bad=await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.product_stock s LEFT JOIN public.inventory_balance_projections b ON b.\"TenantId\"=s.\"TenantId\" AND b.\"BatchId\"=s.\"Id\" WHERE s.\"TenantId\"={tenant} AND (b.\"Id\" IS NULL OR b.\"Quantity\" IS DISTINCT FROM s.\"Quantity\" OR s.\"Quantity\" IS DISTINCT FROM(SELECT sum(e.\"QuantityDelta\") FROM public.inventory_ledger_entries e WHERE e.\"TenantId\"=s.\"TenantId\" AND e.\"BatchId\"=s.\"Id\"))").SingleAsync();Assert.Equal(0,bad);
    }
    [ErpPostgresFact]
    public async Task POS_and_production_skip_held_FEFO_batches_consistently()
    {
        var p=await PosInventoryPostingPostgresTests.Seed(fixture,true);await Hold(p.S.Batch);
        await using(var db=fixture.Context()){var result=await PosInventoryPostingPostgresTests.Service(db,p).CreateSaleAsync(p.S.Tenant,p.S.Actor,p.Request());Assert.Null(result.Error);}
        await using(var db=fixture.Context()){Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(s=>s.Id==p.S.Batch)).Quantity);Assert.Equal(3,(await db.ProductStocks.SingleAsync(s=>s.Id==p.Source.SecondBatch)).Quantity);}
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);await Hold(s.S.Batch);
        await using(var db=fixture.Context()){(await db.RecipeIngredients.SingleAsync(i=>i.RecipeId==s.RecipeId)).Qty=3;await db.SaveChangesAsync();}
        var reader=new ProductionPostingPreviewReader(Options,s.Stock.Gate,new(true),TimeProvider.System);var plan=await reader.ReadAsync(s.Scope,s.OrderId,default);
        Assert.True(plan.CanPost);Assert.Single(plan.Materials);Assert.Equal(s.Stock.SecondBatch,plan.Materials[0].BatchId);
        await new ProductionPostingStore(Options,s.Stock.Gate,new(true),TimeProvider.System,reader).CompleteAsync(s.Scope,s.OrderId,new(Guid.NewGuid(),plan.Fingerprint,"Reviewed safe production material allocation"),default);
        await AssertReconciled(p.S.Tenant);await AssertReconciled(s.S.Tenant);
    }
    [ErpPostgresFact]
    public async Task Transfer_dispatch_rejects_held_stock_and_receive_inherits_a_later_source_hold()
    {
        var s=await TransferPostingPostgresTests.Seed(fixture);var store=TransferPostingPostgresTests.Store(fixture,s);var command=await TransferPostingPostgresTests.Reviewed(store,s);
        await Hold(s.Source.Batch);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DispatchAsync(s.Scope,command,default));
        var other=await TransferPostingPostgresTests.Seed(fixture);store=TransferPostingPostgresTests.Store(fixture,other);command=await TransferPostingPostgresTests.Reviewed(store,other);
        var dispatched=await store.DispatchAsync(other.Scope,command,default);await Hold(other.Source.Batch);
        var reviewed=await store.PreviewAsync(other.Scope,dispatched.TransferId,"receive",default);await store.CompleteAsync(other.Scope,dispatched.TransferId,"receive",new(reviewed.Fingerprint),default);
        await using var check=fixture.Context();Assert.Equal("temp_violation",(await check.ProductStocks.SingleAsync(b=>b.TenantId==other.Source.Tenant&&b.SourceType=="transfer"&&b.SourceId==dispatched.TransferId)).Status);
        await AssertReconciled(other.Source.Tenant);
    }
    [ErpPostgresFact]
    public async Task Write_off_and_reversal_keep_the_hold_even_when_the_batch_was_depleted()
    {
        var s=await WriteOffPostingPostgresTests.Seed(fixture);await Hold(s.Inventory.Batch);
        await using(var db=fixture.Context()){await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.write_off_items SET \"Quantity\"=3.25,\"LossAmount\"=65,\"LossAmountPurchase\"=40.63 WHERE \"WriteOffId\"={s.WriteOffId}");var w=await db.WriteOffs.SingleAsync(w=>w.Id==s.WriteOffId);w.TotalLossAmount=65;w.TotalLossAmountPurchase=40.63m;await db.SaveChangesAsync();}
        var store=WriteOffPostingPostgresTests.Store(s);var review=await store.PreviewAsync(s.Scope,s.WriteOffId,"approve",default);
        await store.PostAsync(s.Scope,s.WriteOffId,"approve",new(review.Fingerprint),default);
        await using(var db=fixture.Context()){var stock=await db.ProductStocks.SingleAsync(b=>b.Id==s.Inventory.Batch);Assert.Equal(0,stock.Quantity);Assert.Equal("temp_violation",stock.Status);}
        var reverse=await store.PreviewAsync(s.Scope,s.WriteOffId,"reverse",default);await store.PostAsync(s.Scope,s.WriteOffId,"reverse",new(reverse.Fingerprint,"Correct disposal after physical stock verification"),default);
        await using(var db=fixture.Context()){var stock=await db.ProductStocks.SingleAsync(b=>b.Id==s.Inventory.Batch);Assert.Equal(3.25m,stock.Quantity);Assert.Equal("temp_violation",stock.Status);}
        await AssertReconciled(s.Inventory.Tenant);
    }
    [ErpPostgresFact]
    public async Task Return_restores_quantity_without_releasing_a_hold_added_after_the_sale()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await PosReturnPostgresTests.Sale(fixture,s);await Hold(s.S.Batch);
        // The existing POS return store uses a no-op fiscal service in this fixture.
        var factory=NSubstitute.Substitute.For<ShelfGuard.Application.Features.Pos.Fiscal.IFiscalServiceFactory>();
        factory.GetForTenantAsync(NSubstitute.Arg.Any<Guid>(),NSubstitute.Arg.Any<CancellationToken>()).Returns(Task.FromResult<ShelfGuard.Application.Features.Pos.Fiscal.IFiscalService>(new ShelfGuard.Application.Features.Pos.Fiscal.NoopFiscalService()));
        var store=new PosReturnStore(Options,s.Source.Gate,new(true),factory);var review=await store.PreviewAsync(s.Scope,sale,s.Shift,default);
        await store.PostAsync(s.Scope,sale,new(s.Shift,Guid.NewGuid(),review.Fingerprint,"Customer returned the complete original purchase"),default);
        await using var db=fixture.Context();Assert.Equal("temp_violation",(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Status);await AssertReconciled(s.S.Tenant);
    }
    [ErpPostgresFact]
    public async Task Quality_release_is_versioned_audited_owner_only_and_quantity_neutral()
    {
        var s=await InventoryFenceScenario.Seed(fixture);await Hold(s.Batch);await using var db=fixture.Context();
        var version=await db.Database.SqlQuery<string>($"SELECT xmin::text AS \"Value\" FROM public.product_stock WHERE \"Id\"={s.Batch}").SingleAsync();
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.product_stock SET \"Status\"='safe' WHERE \"Id\"={s.Batch}"));
        var id=await db.Database.SqlQuery<Guid>($"SELECT public.erp_w4_release_batch_hold({s.Tenant},{s.Batch},{s.Actor},{version},{"Verified physical batch and approved quality release"}) AS \"Value\"").SingleAsync();
        Assert.Equal("quality_release",(await db.StockEvents.SingleAsync(e=>e.Id==id)).EventType);Assert.NotEqual("temp_violation",(await db.ProductStocks.SingleAsync(b=>b.Id==s.Batch)).Status);
        Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.Batch)).Quantity);await AssertReconciled(s.Tenant);
        Assert.Equal("P4F01",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM public.stock_events WHERE \"Id\"={id}"))).SqlState);
    }
    private async Task<(Guid Device,string Physical)> Sensor(InventoryFenceScenario s)
    {
        var physical="integrated-scale-"+Guid.NewGuid().ToString("N");var device=new IotDevice{TenantId=s.Tenant,StoreId=s.Location,DeviceType="weight_sensor",DeviceId=physical,
            Config=JsonSerializer.Serialize(new{product_id=s.Item,inventory_unit="kg",unit_weight_grams=1000})};
        await using var db=fixture.Context();db.Add(device);await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.iot_inventory_policies(\"TenantId\",\"DeviceId\",\"Version\",\"Enabled\",\"ActorId\",\"ItemId\",\"LocationId\",\"Unit\",\"UnitWeightGrams\",\"AccountingMode\",\"Reason\") VALUES({s.Tenant},{device.Id},1,true,{s.Actor},{s.Item},{s.Location},{"kg"},1000,{"automatic_write_down"},{"Synthetic integration scale authorization"})");
        return(device.Id,physical);
    }
    private async Task<string> Observe(InventoryFenceScenario s,(Guid Device,string Physical) sensor,string eventId)
    {
        var now=DateTime.UtcNow;var at=new DateTime(now.Ticks-now.Ticks%TimeSpan.TicksPerMillisecond,DateTimeKind.Utc);
        var payload=JsonSerializer.Serialize(new{device_id=sensor.Physical,event_id=eventId,timestamp=at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),weight_before=(decimal?)null,weight_after=(decimal?)null,delta=-1000,temperature=(decimal?)null,humidity=(decimal?)null,battery=(int?)null});
        static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        await using var db=fixture.Context();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role','worker',true),set_config('app.tenant_id',{s.Tenant.ToString()},true),set_config('app.user_id','',true),set_config('app.erp_posting_id','',true)");
        var receipt=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.iot_message_receipts(\"Id\",\"TenantId\",\"DeviceId\",\"MessageKey\",\"PayloadHash\",\"RecordedAt\") VALUES({receipt},{s.Tenant},{sensor.Device},{Hash("id:"+eventId)},{Hash(payload)},{at})");
        var result=await db.Database.SqlQuery<string>($"SELECT public.erp_w4_iot_ingest_weight({receipt},{payload},true)::text AS \"Value\"").SingleAsync();await tx.CommitAsync();
        using var json=JsonDocument.Parse(result);Assert.False(json.RootElement.GetProperty("applied").GetBoolean());return json.RootElement.GetProperty("reason").GetString()!;
    }
    [ErpPostgresTheory][InlineData(true)][InlineData(false)]
    public async Task POS_is_the_only_writer_whether_the_sensor_arrives_before_or_after_the_sale(bool sensorFirst)
    {
        var p=await PosInventoryPostingPostgresTests.Seed(fixture);
        var device=new IotDevice{TenantId=p.S.Tenant,StoreId=p.S.Location,DeviceType="weight_sensor",DeviceId="pos-scale-"+Guid.NewGuid().ToString("N"),Config=JsonSerializer.Serialize(new{product_id=p.S.Item,inventory_unit="l",unit_weight_grams=1000})};
        await using(var db=fixture.Context()){db.Add(device);await db.SaveChangesAsync();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.iot_inventory_policies(\"TenantId\",\"DeviceId\",\"Version\",\"Enabled\",\"ActorId\",\"ItemId\",\"LocationId\",\"Unit\",\"UnitWeightGrams\",\"AccountingMode\",\"Reason\") VALUES({p.S.Tenant},{device.Id},1,true,{p.S.Actor},{p.S.Item},{p.S.Location},{"l"},1000,{"automatic_write_down"},{"POS owns the stock delta even with automatic sensor policy"})");}
        var sensor=(device.Id,device.DeviceId);
        if(sensorFirst)Assert.Equal("pos_observation",await Observe(p.S,sensor,"before-sale"));
        await using(var db=fixture.Context()){Assert.Null((await PosInventoryPostingPostgresTests.Service(db,p).CreateSaleAsync(p.S.Tenant,p.S.Actor,p.Request())).Error);}
        if(!sensorFirst)Assert.Equal("pos_observation",await Observe(p.S,sensor,"after-sale"));
        await using(var db=fixture.Context()){Assert.Equal(2.25m,(await db.ProductStocks.SingleAsync(b=>b.Id==p.S.Batch)).Quantity);Assert.Equal(1,await db.BusinessDocuments.CountAsync(d=>d.TenantId==p.S.Tenant&&d.SourceSystem=="pos.sale.v1"));Assert.False(await db.BusinessDocuments.AnyAsync(d=>d.TenantId==p.S.Tenant&&d.SourceSystem=="iot.weight.v1"));}
        await AssertReconciled(p.S.Tenant);
    }
    [ErpPostgresFact]
    public async Task Receipt_transfer_production_sale_sensor_and_return_preserve_one_reconciled_ledger()
    {
        var r=await ReceiptPostingPostgresTests.Seed(fixture,1);var tenant=r.Inventory.Tenant;var actor=r.Inventory.Actor;var from=r.Inventory.Location;
        await using(var db=fixture.Context()){(await db.Tenants.SingleAsync(t=>t.Id==tenant)).UpdateModules(["inventory","procurement","production","pos"]);await db.SaveChangesAsync();}
        await ReceiptPostingPostgresTests.Store(fixture,r).ReceiveAsync(r.Scope,r.Receipt,r.Fingerprint,default);
        var dest=new Location{TenantId=tenant,Name="Integrated production warehouse"};var baseline=new ProductStock{TenantId=tenant,StoreId=dest.Id,ProductId=r.Inventory.Item,Quantity=1,QuantityInitial=1,ExpiryDate=new(2028,1,1)};
        await using(var db=fixture.Context()){db.AddRange(dest,baseline,UserLocation.Create(tenant,actor,dest.Id,null));await db.SaveChangesAsync();}
        await using(var db=fixture.Context()){var snapshot=await new InventorySnapshotReader(db).ReadAsync(tenant,dest.Id,operatorId:actor);var report=new InventoryDryRunAnalyzer().Analyze(snapshot);await new ControlBalanceMaterializer(db).MaterializeAsync(new(tenant,dest.Id,Guid.NewGuid(),report.SourceFingerprint,[baseline.Id],"Reviewed integrated destination opening","integrated-test",OperatorId:actor));await InventoryFenceTestHelper.Activate(db,tenant,dest.Id,actor);}
        var scope=r.Scope with{LocationIds=[from,dest.Id]};var gate=new InventoryPostingGate(true,[new(tenant,from),new(tenant,dest.Id)]);var transfers=new TransferPostingStore(Options,gate,TimeProvider.System,new(true));
        Guid incoming;await using(var db=fixture.Context())incoming=(await db.ProductStocks.SingleAsync(b=>b.TenantId==tenant&&b.SourceId==r.Receipt)).Id;
        var command=new TransferDispatchCommand(Guid.NewGuid(),from,dest.Id,"store_to_store","Integrated raw material shipment",[new(incoming,2)]);command=command with{ExpectedFingerprint=(await transfers.PreviewDispatchAsync(scope,command,default)).Fingerprint};
        var sent=await transfers.DispatchAsync(scope,command,default);var receiving=await transfers.PreviewAsync(scope,sent.TransferId,"receive",default);await transfers.CompleteAsync(scope,sent.TransferId,"receive",new(receiving.Fingerprint),default);
        var barcode="integrated-output-"+Guid.NewGuid().ToString("N");var item=new Item{TenantId=tenant,Name="Integrated finished item",Unit="kg",ShelfLifeDays=30,PriceRetail=50,PricePurchase=999,VatRate=20,Barcodes=[barcode]};
        var recipe=new Recipe{TenantId=tenant,OutputItemId=item.Id,Name="Integrated recipe",Unit="kg",OutputQty=2};recipe.Ingredients.Add(new(){RecipeId=recipe.Id,ItemId=r.Inventory.Item,Qty=2,Unit="kg"});
        var order=new ProductionOrder{TenantId=tenant,RecipeId=recipe.Id,LocationId=dest.Id,PlannedQty=2,Status=ProductionOrderStatus.InProgress,StartedAt=DateTimeOffset.UtcNow};var shift=new PosShift{TenantId=tenant,StoreId=dest.Id,CashierId=actor,ShiftNumber=1};
        await using(var db=fixture.Context()){db.AddRange(item,recipe,order,shift);await db.SaveChangesAsync();}
        var reader=new ProductionPostingPreviewReader(Options,gate,new(true),TimeProvider.System);var plan=await reader.ReadAsync(scope,order.Id,default);Assert.Equal(12.5m,plan.Output.UnitCost);
        var made=await new ProductionPostingStore(Options,gate,new(true),TimeProvider.System,reader).CompleteAsync(scope,order.Id,new(Guid.NewGuid(),plan.Fingerprint,"Verified integrated production and FEFO material cost"),default);
        var finished=new InventoryFenceScenario(tenant,dest.Id,actor,item.Id,made.OutputBatchId);var pos=new PosInventoryPostingPostgresTests.Scenario(new(finished,Guid.Empty,null),shift.Id,barcode);
        Guid sale;await using(var db=fixture.Context()){var result=await PosInventoryPostingPostgresTests.Service(db,pos).CreateSaleAsync(tenant,actor,pos.Request());Assert.Null(result.Error);sale=result.Sale!.TransactionId;}
        Assert.Equal("pos_observation",await Observe(finished,await Sensor(finished),"same-physical-sale"));
        var fiscal=NSubstitute.Substitute.For<ShelfGuard.Application.Features.Pos.Fiscal.IFiscalServiceFactory>();fiscal.GetForTenantAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult<ShelfGuard.Application.Features.Pos.Fiscal.IFiscalService>(new ShelfGuard.Application.Features.Pos.Fiscal.NoopFiscalService()));
        var returns=new PosReturnStore(Options,gate,new(true),fiscal);var back=await returns.PreviewAsync(scope,sale,shift.Id,default);await returns.PostAsync(scope,sale,new(shift.Id,Guid.NewGuid(),back.Fingerprint,"Full customer return in integrated acceptance"),default);
        await using(var db=fixture.Context()){Assert.Equal(2,(await db.ProductStocks.SingleAsync(b=>b.Id==made.OutputBatchId)).Quantity);Assert.Equal(0,(await db.PosShifts.SingleAsync(s=>s.Id==shift.Id)).TotalSales);Assert.False(await db.BusinessDocuments.AnyAsync(d=>d.TenantId==tenant&&d.SourceSystem=="iot.weight.v1"));}
        await AssertReconciled(tenant);
    }
    [ErpPostgresFact]
    public async Task Bounded_parallel_sales_replay_without_duplicate_postings_or_balance_drift()
    {
        var cases=new List<PosInventoryPostingPostgresTests.Scenario>();for(var i=0;i<8;i++)cases.Add(await PosInventoryPostingPostgresTests.Seed(fixture));
        var watch=Stopwatch.StartNew();
        await Task.WhenAll(cases.Select(async s=>{var command=s.Request();for(var attempt=0;;attempt++){await using var db=fixture.Context();var result=await PosInventoryPostingPostgresTests.Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,command);if(result.Error==null)break;if(result.StatusCode!=409||attempt>=2)Assert.Fail(result.Error);await Task.Delay(50);}await using var retry=fixture.Context();Assert.Null((await PosInventoryPostingPostgresTests.Service(retry,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,command)).Error);}));
        watch.Stop();
        foreach(var s in cases){await AssertReconciled(s.S.Tenant);await using var db=fixture.Context();Assert.Equal(2.25m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);Assert.Equal(1,await db.DocumentPostings.CountAsync(p=>p.TenantId==s.S.Tenant&&p.IdempotencyKey.StartsWith("pos.sale.v1")));}
        Assert.True(watch.Elapsed<TimeSpan.FromMinutes(3),"Bounded acceptance deadline exceeded.");
    }
    [ErpPostgresTheory][InlineData("stale")][InlineData("expired")][InlineData("unauthorized")]
    public async Task Invalid_quality_decision_cannot_release_the_hold(string kind)
    {
        var s=await InventoryFenceScenario.Seed(fixture);await Hold(s.Batch);await using var db=fixture.Context();
        if(kind=="expired")await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.product_stock SET \"ExpiryDate\"=CURRENT_DATE-1 WHERE \"Id\"={s.Batch}");
        var version=kind=="stale"?"0":await db.Database.SqlQuery<string>($"SELECT xmin::text AS \"Value\" FROM public.product_stock WHERE \"Id\"={s.Batch}").SingleAsync();
        if(kind=="unauthorized")
        {
            await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlRawAsync("SET LOCAL ROLE "+fixture.ReaderRole);
            Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.SqlQuery<Guid>($"SELECT public.erp_w4_release_batch_hold({s.Tenant},{s.Batch},{s.Actor},{version},{"Verified physical batch and approved quality release"}) AS \"Value\"").SingleAsync())).SqlState);await tx.RollbackAsync();
        }
        else await Assert.ThrowsAsync<PostgresException>(()=>db.Database.SqlQuery<Guid>($"SELECT public.erp_w4_release_batch_hold({s.Tenant},{s.Batch},{s.Actor},{version},{"Verified physical batch and approved quality release"}) AS \"Value\"").SingleAsync());
        Assert.Equal("temp_violation",(await db.ProductStocks.SingleAsync(b=>b.Id==s.Batch)).Status);
    }
    [ErpPostgresFact]
    public async Task Integrated_readiness_fails_closed_when_a_quality_guard_is_disabled()
    {
        var s=await InventoryFenceScenario.Seed(fixture);await using var db=fixture.Context();await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE public.stock_events DISABLE TRIGGER erp_w4_quality_event_guard");
        Assert.True(await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_scope_ready({s.Tenant},{s.Location}) AS \"Value\"").SingleAsync());
        Assert.False(await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({s.Tenant},{s.Location}) AS \"Value\"").SingleAsync());await tx.RollbackAsync();
    }
}
