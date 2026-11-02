using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class PosReturnPostgresTests(ErpSchemaFixture fixture)
{
    private DbContextOptions<AppDbContext> Options(params IInterceptor[] interceptors)=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(interceptors).IgnoreManyServiceProvidersWarning().Options;
    private PosReturnStore Store(PosInventoryPostingPostgresTests.Scenario s,bool enabled=true,IFiscalService? fiscal=null,params IInterceptor[] interceptors)
    {
        var factory=Substitute.For<IFiscalServiceFactory>();factory.GetForTenantAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(fiscal??new NoopFiscalService());
        return new(Options(interceptors),s.Source.Gate,new(enabled),factory);
    }
    internal static async Task<Guid> Sale(ErpSchemaFixture f,PosInventoryPostingPostgresTests.Scenario s,decimal qty=1)
    {
        await using var db=f.Context();var result=await PosInventoryPostingPostgresTests.Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request(qty));Assert.Null(result.Error);return result.Sale!.TransactionId;
    }
    private async Task<PosReturnCommand> Review(PosInventoryPostingPostgresTests.Scenario s,Guid saleId)
    {var p=await Store(s).PreviewAsync(s.Scope,saleId,s.Shift,default);return new(s.Shift,Guid.NewGuid(),p.Fingerprint,"Customer returned the entire original receipt");}
    [ErpPostgresFact]
    public async Task Full_multi_batch_return_exactly_reverses_ledger_and_paid_money_and_replays()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture,true);var sale=await Sale(fixture,s,4.5m);var command=await Review(s,sale);var store=Store(s);
        var a=await store.PostAsync(s.Scope,sale,command,default);var b=await store.PostAsync(s.Scope,sale,command,default);
        Assert.False(a.Replayed);Assert.True(b.Replayed);Assert.Equal(a.DocumentId,b.DocumentId);Assert.Equal(225,a.RefundAmount);
        await using var check=fixture.Context();var original=await check.BusinessDocuments.SingleAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosInventoryPostingBridge.Source);Assert.Equal("reversed",original.Status);
        var entries=await check.InventoryLedgerEntries.Where(e=>e.DocumentId==a.DocumentId).ToListAsync();Assert.Equal(2,entries.Count);Assert.All(entries,e=>Assert.NotNull(e.ReversesEntryId));
        Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);Assert.Equal(4,(await check.ProductStocks.SingleAsync(x=>x.Id==s.Source.SecondBatch)).Quantity);
        Assert.Equal(0,(await check.PosShifts.SingleAsync(x=>x.Id==s.Shift)).TotalSales);Assert.Equal(0,await new PosRepository(check).GetCashSalesTotalForShiftAsync(s.Shift));
        Assert.Equal(2,await check.OutboxEvents.CountAsync(x=>x.TenantId==s.S.Tenant&&x.DeliveryScope=="live"));
        var job=await check.Set<PosReturnFiscalization>().SingleAsync(x=>x.Id==a.DocumentId);Assert.Equal("pending",job.Status);Assert.Equal(37.5m,job.TaxAmount);
        var request=JsonSerializer.Deserialize<FiscalReceiptRequest>(job.RequestJson)!;Assert.True(request.IsReturn);Assert.Equal(sale.ToString(),request.RelatedReceiptId);Assert.Equal(a.DocumentId.ToString(),request.LocalReceiptId);
    }
    [ErpPostgresTheory][InlineData("client")][InlineData("reason")][InlineData("technical")]
    public async Task Different_replay_is_a_conflict_and_stock_is_not_returned_again(string change)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var c=await Review(s,sale);await Store(s).PostAsync(s.Scope,sale,c,default);
        var other=change=="client"?c with{ClientRequestId=Guid.NewGuid()}:change=="reason"?c with{Reason="Different reason for this full return"}:c with{TechnicalReturn=true};
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PostAsync(s.Scope,sale,other,default));
    }
    [ErpPostgresTheory][InlineData("stale")][InlineData("disabled")][InlineData("permission")][InlineData("closed")][InlineData("unit")]
    public async Task Changed_stock_live_rights_closed_shift_and_disabled_gate_are_atomic(string kind)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var c=await Review(s,sale);
        if(kind=="stale")await Sale(fixture,s);
        await using(var change=fixture.Context())
        {
            if(kind=="permission")(await change.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"pos",false}});
            if(kind=="closed")(await change.PosShifts.SingleAsync(x=>x.Id==s.Shift)).ClosedAt=DateTime.UtcNow;
            if(kind=="unit")(await change.Items.SingleAsync(x=>x.Id==s.S.Item)).Unit="box";
            await change.SaveChangesAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(()=>Store(s,kind!="disabled").PostAsync(s.Scope,sale,c,default));
        await using var check=fixture.Context();Assert.False(await check.Set<PosReturnFiscalization>().AnyAsync(j=>j.TenantId==s.S.Tenant));
        Assert.Equal(kind=="stale"?1.25m:2.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresTheory][InlineData("outbox")][InlineData("job")][InlineData("seal")]
    public async Task Failure_after_staging_rolls_back_the_entire_return(string phase)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var c=await Review(s,sale);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Store(s,true,null,new Fail(phase)).PostAsync(s.Scope,sale,c,default));
        await using var check=fixture.Context();Assert.Equal(2.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);Assert.Equal(50,(await check.PosShifts.SingleAsync(x=>x.Id==s.Shift)).TotalSales);
        Assert.False(await check.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==PosReturnStore.Source));
    }
    private sealed class Fail(string phase):SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
        {
            var db=e.Context!;
            if(phase=="outbox"&&db.ChangeTracker.Entries<OutboxEvent>().Any(x=>x.State==EntityState.Added)||phase=="job"&&db.ChangeTracker.Entries<PosReturnFiscalization>().Any(x=>x.State==EntityState.Added)||phase=="seal"&&db.ChangeTracker.Entries<BusinessDocument>().Any(x=>x.Entity.SourceSystem==PosReturnStore.Source&&x.Entity.Status=="posted"))throw new InvalidOperationException("synthetic return fault");
            return ValueTask.FromResult(result);
        }
    }
    [ErpPostgresFact]
    public async Task Concurrent_identical_return_returns_the_same_durable_result()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var c=await Review(s,sale);
        var results=await Task.WhenAll(Store(s).PostAsync(s.Scope,sale,c,default),Store(s).PostAsync(s.Scope,sale,c,default));
        Assert.Single(results,r=>!r.Replayed);Assert.Single(results,r=>r.Replayed);Assert.Equal(results[0].DocumentId,results[1].DocumentId);
    }
    [ErpPostgresTheory][InlineData("money")][InlineData("payload")][InlineData("delete")][InlineData("truncate")]
    public async Task Durable_fiscal_compensation_source_is_immutable(string kind)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        await using var db=fixture.Context();var q=kind=="money"?"UPDATE pos_return_fiscalizations SET \"Amount\"=0":kind=="payload"?"UPDATE pos_return_fiscalizations SET \"RequestJson\"=jsonb_build_object()":kind=="delete"?"DELETE FROM pos_return_fiscalizations":"TRUNCATE pos_return_fiscalizations";
        Assert.Equal("P4F01",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(q))).SqlState);
    }
    [ErpPostgresFact]
    public async Task Fiscal_unknown_outcome_is_recovered_by_same_UUID_without_second_refund_or_stock_posting()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var result=await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        await using(var db=fixture.Context()){var t=await db.PosTransactions.SingleAsync(t=>t.Id==sale);t.Status="fiscalized";t.FiscalNumber="synthetic-original";await db.SaveChangesAsync();}
        var fiscal=Substitute.For<IFiscalService>();var reads=0;
        fiscal.GetReceiptStatusAsync(sale.ToString(),Arg.Any<CancellationToken>()).Returns(new FiscalReceiptResult(sale.ToString(),FiscalReceiptStatus.Done,"synthetic-original",null,50,null));
        fiscal.GetReceiptStatusAsync(result.DocumentId.ToString(),Arg.Any<CancellationToken>()).Returns(_=>{
            if(Interlocked.Increment(ref reads)==1)throw new FiscalProviderException("not found",httpStatus:404);
            return Task.FromResult(new FiscalReceiptResult(result.DocumentId.ToString(),FiscalReceiptStatus.Done,"synthetic-return",null,50,null));});
        fiscal.CreateReceiptAsync(Arg.Any<FiscalReceiptRequest>(),Arg.Any<CancellationToken>()).Returns<Task<FiscalReceiptResult>>(_=>throw new FiscalProviderException("unknown outcome",isTransient:true));
        var store=Store(s,true,fiscal);Assert.Equal("pending",(await store.FiscalizeAsync(s.Scope,sale,default)).Status);Assert.Equal("done",(await store.FiscalizeAsync(s.Scope,sale,default)).Status);
        Assert.Equal("done",(await store.FiscalizeAsync(s.Scope,sale,default)).Status);await fiscal.Received(1).CreateReceiptAsync(Arg.Any<FiscalReceiptRequest>(),Arg.Any<CancellationToken>());
        await using var check=fixture.Context();Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);Assert.Equal(2,await check.OutboxEvents.CountAsync(o=>o.TenantId==s.S.Tenant&&o.DeliveryScope=="live"));
    }
    [ErpPostgresFact]
    public async Task Pending_original_fiscalization_never_sends_a_refund_receipt()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        var fiscal=Substitute.For<IFiscalService>();var r=await Store(s,true,fiscal).FiscalizeAsync(s.Scope,sale,default);Assert.Equal(0,r.Attempts);
        await fiscal.DidNotReceiveWithAnyArgs().CreateReceiptAsync(default!,default);
    }
    [ErpPostgresFact]
    public async Task Sold_out_ERP_batch_still_has_a_complete_return_plan()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s,3.25m);
        var p=await Store(s).PreviewAsync(s.Scope,sale,s.Shift,default);Assert.Equal(0,p.Batches.Single().CurrentQuantity);Assert.Equal(3.25m,p.Batches.Single().Quantity);
    }
    [ErpPostgresFact]
    public async Task Loyalty_receipt_is_blocked_without_modifying_bonus_lots_or_inventory()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var consumer=new ConsumerAccount{Phone="+380"+Guid.NewGuid().ToString("N")[..9],PasswordHash="hash",FullName="Synthetic buyer"};
        var membership=new LoyaltyMembership{TenantId=s.S.Tenant,ConsumerAccountId=consumer.Id,TotpSecret="SYNTHETIC",Balance=100};
        Guid sale;
        await using(var seed=fixture.Context()){seed.AddRange(consumer,membership,new LoyaltyProgramSettings{TenantId=s.S.Tenant,AccrualRatePercent=3,RedemptionCapPercent=100});await seed.SaveChangesAsync();}
        await using(var db=fixture.Context()){var result=await PosInventoryPostingPostgresTests.Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request() with{LoyaltyMembershipId=membership.Id,RedeemAmount=10});Assert.Null(result.Error);sale=result.Sale!.TransactionId;}
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PreviewAsync(s.Scope,sale,s.Shift,default));
        await using var check=fixture.Context();Assert.Equal(91.20m,(await check.LoyaltyMemberships.SingleAsync(m=>m.Id==membership.Id)).Balance);Assert.False(await check.Set<PosReturnFiscalization>().AnyAsync(j=>j.TenantId==s.S.Tenant));
    }
    [ErpPostgresFact]
    public async Task Closed_original_shift_can_be_refunded_in_new_shift_and_stock_history_counts_both_once()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);
        var shift=new PosShift{TenantId=s.S.Tenant,StoreId=s.S.Location,CashierId=s.S.Actor,ShiftNumber=2};
        await using(var db=fixture.Context()){(await db.PosShifts.SingleAsync(x=>x.Id==s.Shift)).ClosedAt=DateTime.UtcNow;db.Add(shift);await db.SaveChangesAsync();}
        var store=Store(s);var p=await store.PreviewAsync(s.Scope,sale,shift.Id,default);
        await store.PostAsync(s.Scope,sale,new(shift.Id,Guid.NewGuid(),p.Fingerprint,"Customer returned a receipt from the previous shift"),default);
        await using var check=fixture.Context();Assert.Equal(-50,await new PosRepository(check).GetCashSalesTotalForShiftAsync(shift.Id));
        Assert.Equal(50,(await check.PosShifts.SingleAsync(x=>x.Id==s.Shift)).TotalSales);Assert.Equal(-50,(await check.PosShifts.SingleAsync(x=>x.Id==shift.Id)).TotalSales);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);var history=await new MovementRepository(check).GetStockHistoryAsync(s.S.Tenant,s.S.Item,s.S.Location,today);
        Assert.Equal(3.25m,history.CurrentStock);Assert.Equal(3.25m,history.DailyChanges[today]);
    }
    [ErpPostgresTheory][InlineData("identity")][InlineData("amount")][InlineData("error")]
    public async Task Fiscal_provider_mismatch_or_rejection_requires_reconciliation_without_a_second_inventory_action(string kind)
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);var r=await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        await using(var db=fixture.Context()){var t=await db.PosTransactions.SingleAsync(x=>x.Id==sale);t.Status="fiscalized";t.FiscalNumber="original";await db.SaveChangesAsync();}
        var fiscal=Substitute.For<IFiscalService>();fiscal.GetReceiptStatusAsync(sale.ToString(),Arg.Any<CancellationToken>()).Returns(new FiscalReceiptResult(sale.ToString(),FiscalReceiptStatus.Done,"original",null,50,null));
        fiscal.GetReceiptStatusAsync(r.DocumentId.ToString(),Arg.Any<CancellationToken>()).Returns(new FiscalReceiptResult(kind=="identity"?Guid.NewGuid().ToString():r.DocumentId.ToString(),kind=="error"?FiscalReceiptStatus.Error:FiscalReceiptStatus.Done,"return",null,kind=="amount"?49:50,null));
        var result=await Store(s,true,fiscal).FiscalizeAsync(s.Scope,sale,default);Assert.Equal("failed",result.Status);
        await fiscal.DidNotReceiveWithAnyArgs().CreateReceiptAsync(default!,default);
        await using var check=fixture.Context();Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Fiscal_return_table_is_fail_closed_tenant_scoped_and_has_no_provider_bypass()
    {
        var own=await PosInventoryPostingPostgresTests.Seed(fixture);var foreign=await PosInventoryPostingPostgresTests.Seed(fixture);
        var a=await Sale(fixture,own);var b=await Sale(fixture,foreign);
        var first=await Store(own).PostAsync(own.Scope,a,await Review(own,a),default);await Store(foreign).PostAsync(foreign.Scope,b,await Review(foreign,b),default);
        await using var db=fixture.Context();await using var tx=await db.Database.BeginTransactionAsync();
        // The identifier is generated by this disposable fixture, never supplied by a client.
        var setRole=$"SET LOCAL ROLE {fixture.ReaderRole}";
        await db.Database.ExecuteSqlRawAsync(setRole);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id=''; SET LOCAL app.role=''");Assert.Empty(await db.Set<PosReturnFiscalization>().ToListAsync());
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role='provider'");Assert.Empty(await db.Set<PosReturnFiscalization>().ToListAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{own.S.Tenant.ToString()},true),set_config('app.role','store_manager',true)");
        var rows=await db.Set<PosReturnFiscalization>().AsNoTracking().ToListAsync();Assert.Single(rows);Assert.Equal(first.DocumentId,rows[0].Id);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role='worker'");var tenants=await db.Set<PosReturnFiscalization>().Select(j=>j.TenantId).ToListAsync();Assert.Contains(own.S.Tenant,tenants);Assert.Contains(foreign.S.Tenant,tenants);
    }
    [ErpPostgresFact]
    public async Task Concurrent_fiscal_claim_timeout_is_a_retryable_conflict_without_changing_inventory()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var sale=await Sale(fixture,s);
        var r=await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        await using var owner=fixture.Context();await using var tx=await owner.Database.BeginTransactionAsync();
        await owner.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.pos_return_fiscalizations WHERE \"Id\"={r.DocumentId} FOR UPDATE");
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).FiscalizeAsync(s.Scope,sale,default));
        await tx.RollbackAsync();await using var check=fixture.Context();Assert.Equal(0,(await check.Set<PosReturnFiscalization>().SingleAsync(j=>j.Id==r.DocumentId)).Attempts);
        Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task A_stale_customer_update_cannot_overwrite_totals_after_a_committed_return()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var customer=new Customer{TenantId=s.S.Tenant,Name="Synthetic return customer"};
        await using(var db=fixture.Context()){db.Add(customer);await db.SaveChangesAsync();}
        Guid sale;
        await using(var db=fixture.Context()){var result=await PosInventoryPostingPostgresTests.Service(db,s).CreateSaleAsync(s.S.Tenant,s.S.Actor,s.Request() with{CustomerId=customer.Id});Assert.Null(result.Error);sale=result.Sale!.TransactionId;}
        await using var stale=fixture.Context();var before=await stale.Customers.SingleAsync(c=>c.Id==customer.Id);Assert.Equal(50,before.TotalSpent);before.TotalSpent+=25;before.TotalOrders++;
        await Store(s).PostAsync(s.Scope,sale,await Review(s,sale),default);
        var repo=new PosRepository(stale);
        await Assert.ThrowsAsync<ShelfGuard.Domain.Exceptions.ConcurrencyConflictException>(()=>repo.SaveChangesAsync());
        repo.DiscardPendingChanges();Assert.Empty(stale.ChangeTracker.Entries());
        await using var check=fixture.Context();var after=await check.Customers.SingleAsync(c=>c.Id==customer.Id);Assert.Equal(0,after.TotalOrders);Assert.Equal(0,after.TotalSpent);
    }
    [ErpPostgresFact]
    public async Task Crossing_client_keys_of_two_existing_returns_is_a_conflict_without_a_second_refund()
    {
        var s=await PosInventoryPostingPostgresTests.Seed(fixture);var a=await Sale(fixture,s);var b=await Sale(fixture,s);
        var first=await Review(s,a);await Store(s).PostAsync(s.Scope,a,first,default);
        var second=await Review(s,b);await Store(s).PostAsync(s.Scope,b,second,default);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PostAsync(s.Scope,a,first with{ClientRequestId=second.ClientRequestId},default));
        await using var check=fixture.Context();Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.S.Batch)).Quantity);Assert.Equal(2,await check.Set<PosReturnFiscalization>().CountAsync(x=>x.TenantId==s.S.Tenant));
    }
}
