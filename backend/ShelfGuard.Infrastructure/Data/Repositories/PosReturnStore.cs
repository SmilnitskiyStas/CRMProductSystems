using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>Full receipt reversal. Never infers allocation from PosTransactionItem.ProductStockId.</summary>
public sealed class PosReturnStore(DbContextOptions<AppDbContext> options, IInventoryPostingGate gate,
    PosReturnOptions writer, IFiscalServiceFactory fiscalFactory) : IPosReturnStore
{
    public const string Source = "pos.return.v1";
    private sealed record Row(InventoryLedgerEntry Original, DocumentLine Line, ProductStock Stock, InventoryBalanceProjection Balance);
    private sealed record Plan(PosTransaction Sale, BusinessDocument Original, DocumentPosting Posting, PosShift Shift, List<Row> Rows, string Snapshot, string Fingerprint);
    public async Task<PosReturnPreview> PreviewAsync(WorkAccessScope scope, Guid saleId, Guid shiftId, CancellationToken ct)
    {
        await using var db = new AppDbContext(options);await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);await Establish(db,scope,ct);
        var p=await Load(db,scope,saleId,shiftId,false,ct);var ready=await Ready(db,scope.TenantId,p.Sale.StoreId,ct);
        return new(saleId,shiftId,p.Fingerprint,p.Sale.TotalAmount,p.Sale.TaxAmount,writer.Enabled&&gate.Allows(scope.TenantId,p.Sale.StoreId)&&ready,
            p.Rows.Select(r=>new PosReturnBatch(r.Original.Id,r.Stock.Id,r.Stock.ProductId,-r.Original.QuantityDelta,r.Stock.Quantity,r.Original.UnitCost)).ToArray());
    }
    public async Task<PosReturnResult> PostAsync(WorkAccessScope scope, Guid saleId, PosReturnCommand command, CancellationToken ct)
    {
        TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
        if(command.ClientRequestId==Guid.Empty||command.ShiftId==Guid.Empty)throw new ArgumentException("Durable client identity and shift required.");
        command=command with {Reason=WriteOffPostingRules.Reason("reverse",command.Reason)!};
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(1));ct=timeout.Token;
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            await Establish(db,scope,ct);
            var location=await db.PosTransactions.Where(t=>t.TenantId==scope.TenantId&&t.Id==saleId).Select(t=>(Guid?)t.StoreId).SingleOrDefaultAsync(ct)??throw new InventoryPostingNotFoundException();
            await Location(db,scope,location,ct);
            if(!writer.Enabled||!gate.Allows(scope.TenantId,location))throw new InventoryPostingDisabledException();
            if(!await Ready(db,scope.TenantId,location,ct))throw Conflict("POS return fence upgrade required.");
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.product_stock IN EXCLUSIVE MODE; LOCK TABLE public.inventory_reservations IN SHARE MODE",ct);
            var hash=ReceiptPostingRules.Hash(new{saleId,command});
            // Either unique identity can match a different existing return. One match
            // suffices to reject the crossed keys; do not treat two matches as a 500.
            var prior=await db.Set<PosReturnFiscalization>().FirstOrDefaultAsync(r=>r.TenantId==scope.TenantId&&(r.SaleId==saleId||r.ClientRequestId==command.ClientRequestId),ct);
            if(prior!=null)
            {
                if(prior.SaleId!=saleId||prior.RequestHash!=hash||prior.ClientRequestId!=command.ClientRequestId)throw Conflict("Different return confirmation.");
                var posting=await db.DocumentPostings.SingleAsync(p=>p.TenantId==scope.TenantId&&p.DocumentId==prior.Id,ct);
                await tx.CommitAsync(ct);return new(saleId,prior.Id,posting.Id,prior.Amount,prior.Status,true);
            }
            var p=await Load(db,scope,saleId,command.ShiftId,true,ct);
            if(p.Fingerprint!=command.ExpectedFingerprint)throw Conflict("Return source or stock changed since review.");
            var time=DateTime.UtcNow;var at=new DateTime(time.Ticks-time.Ticks%10,DateTimeKind.Utc);
            var doc=new BusinessDocument{TenantId=scope.TenantId,DocumentType="pos_return",SourceSystem=Source,SourceId=saleId.ToString(),
                Number="RETURN:"+p.Sale.ReceiptNumber,CreatedBy=scope.UserId,ContentHash=hash,ReversesDocumentId=p.Original.Id,OccurredAt=at,CreatedAt=at,UpdatedAt=at};
            db.Add(doc);await db.SaveChangesAsync(ct);
            var postingNew=new DocumentPosting{TenantId=scope.TenantId,DocumentId=doc.Id,IdempotencyKey=$"{Source}:{saleId:N}",RequestHash=hash,PerformedBy=scope.UserId,PostedAt=at,ReversesPostingId=p.Posting.Id};
            var entries=new List<InventoryLedgerEntry>();
            for(var i=0;i<p.Rows.Count;i++)
            {
                var r=p.Rows[i];var old=r.Original;var delta=-old.QuantityDelta;
                var line=new DocumentLine{TenantId=scope.TenantId,DocumentId=doc.Id,LineNumber=i+1,ItemId=old.ItemId,ToLocationId=old.LocationId,Quantity=delta,
                    Unit=r.Line.Unit,UnitCost=old.UnitCost,ItemNameSnapshot=r.Line.ItemNameSnapshot,BarcodeSnapshot=r.Line.BarcodeSnapshot,
                    BatchNumberSnapshot=old.BatchNumberSnapshot,ExpiryDateSnapshot=old.ExpiryDateSnapshot,
                    Metadata=JsonSerializer.Serialize(new{saleId,originalEntryId=old.Id,source=JsonDocument.Parse(p.Snapshot).RootElement.Clone()})};
                db.Add(line);entries.Add(new(){TenantId=scope.TenantId,DocumentId=doc.Id,PostingId=postingNew.Id,DocumentLineId=line.Id,EntryNumber=i+1,
                    ItemId=old.ItemId,LocationId=old.LocationId,BatchId=old.BatchId,ZoneId=old.ZoneId,QuantityBefore=r.Stock.Quantity,QuantityAfter=r.Stock.Quantity+delta,
                    QuantityDelta=delta,UnitCost=old.UnitCost,ValueDelta=-old.ValueDelta,Currency=old.Currency,BatchNumberSnapshot=old.BatchNumberSnapshot,
                    ExpiryDateSnapshot=old.ExpiryDateSnapshot,ReversesEntryId=old.Id,OccurredAt=at,CreatedAt=at});
            }
            await db.SaveChangesAsync(ct);doc.Status="submitted";await db.SaveChangesAsync(ct);
            db.Add(new DocumentApproval{TenantId=scope.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=scope.UserId,Decision="approved",ContentHash=hash,Reason=command.Reason,CreatedAt=at});
            doc.Status="approved";await db.SaveChangesAsync(ct);db.Add(postingNew);await db.SaveChangesAsync(ct);db.AddRange(entries);await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{postingNew.Id.ToString()},true)",ct);
            for(var i=0;i<p.Rows.Count;i++)
            {
                var r=p.Rows[i];var e=entries[i];r.Stock.Quantity=e.QuantityAfter!.Value;r.Stock.Status=StockStatus.Recalculate(r.Stock);
                r.Balance.Quantity=r.Stock.Quantity;r.Balance.LastLedgerEntryId=e.Id;r.Balance.UpdatedAt=at;
                db.Add(new StockMovement{TenantId=scope.TenantId,ProductStockId=r.Stock.Id,ProductId=r.Stock.ProductId,MovementType="adjustment",ToStoreId=r.Stock.StoreId,
                    ToZoneId=r.Stock.ZoneId,Quantity=e.QuantityDelta,QuantityBefore=e.QuantityBefore,QuantityAfter=e.QuantityAfter,ReferenceType="pos_return",ReferenceId=doc.Id,PerformedBy=scope.UserId,Notes=command.Reason,CreatedAt=at});
            }
            if(p.Sale.CustomerId is Guid customerId)
            {
                var customer=await db.Customers.FromSqlInterpolated($"SELECT *,xmin FROM public.customers WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={customerId} FOR UPDATE").SingleAsync(ct);
                if(customer.TotalOrders<1||customer.TotalSpent<p.Sale.TotalAmount)throw Conflict("Customer totals require reconciliation.");
                customer.TotalOrders--;customer.TotalSpent-=p.Sale.TotalAmount;
            }
            p.Shift.TotalSales-=p.Sale.TotalAmount;
            var fiscalRequest=new FiscalReceiptRequest(p.Sale.Items.OrderBy(i=>i.Id).Select(i=>new FiscalReceiptItem(i.ProductId.ToString(),i.ProductNameSnapshot!,i.PriceFinal,i.Quantity,i.BarcodeSnapshot,i.VatRate)).ToArray(),
                [new(p.Sale.PaymentType.Equals("cash",StringComparison.OrdinalIgnoreCase)?FiscalPaymentKind.Cash:FiscalPaymentKind.Cashless,p.Sale.TotalAmount)],doc.Id.ToString(),IsReturn:true,RelatedReceiptId:saleId.ToString(),TechnicalReturn:command.TechnicalReturn);
            db.Add(new PosReturnFiscalization{Id=doc.Id,TenantId=scope.TenantId,SaleId=saleId,ShiftId=p.Shift.Id,ClientRequestId=command.ClientRequestId,RequestHash=hash,Reason=command.Reason,
                Amount=p.Sale.TotalAmount,TaxAmount=p.Sale.TaxAmount,PaymentType=p.Sale.PaymentType,RequestJson=JsonSerializer.Serialize(fiscalRequest),CreatedAt=at});
            db.Add(new DocumentLink{TenantId=scope.TenantId,FromDocumentId=p.Original.Id,ToDocumentId=doc.Id,RelationType="reverses",CreatedAt=at});
            db.Add(ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(scope.TenantId,Guid.NewGuid(),new(at),new(doc.Id,doc.DocumentKey,doc.Version,postingNew.Id,
                entries.Select(e=>new InventoryLedgerDeltaV1(e.Id,e.EntryNumber,e.ItemId,e.LocationId,e.BatchId,e.QuantityDelta,e.UnitCost,e.ValueDelta,e.Currency,e.ZoneId,e.ReversesEntryId)).ToArray(),p.Posting.Id),correlationId:saleId)));
            await db.SaveChangesAsync(ct);doc.Status="posted";doc.PostedAt=at;p.Original.Status="reversed";p.Original.UpdatedAt=at;
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(saleId,doc.Id,postingNew.Id,p.Sale.TotalAmount,"pending",false);
        }
        catch(Exception ex) when(ex is DbUpdateConcurrencyException || (ex as PostgresException??ex.InnerException as PostgresException)?.SqlState is "P4F01" or "55P03" or "40001" or "40P01" or "23505")
        {throw Conflict("Concurrent or inconsistent POS return; retry the original confirmation.");}
    }
    public async Task<PosReturnFiscalResult> FiscalizeAsync(WorkAccessScope scope,Guid saleId,CancellationToken ct)
    {
        try { return await FiscalizeCoreAsync(scope,saleId,ct); }
        catch(Exception ex) when((ex as PostgresException??ex.InnerException as PostgresException)?.SqlState is "55P03" or "40001" or "40P01")
        { throw Conflict("Concurrent fiscal compensation; retry the same return."); }
    }
    private async Task<PosReturnFiscalResult> FiscalizeCoreAsync(WorkAccessScope scope,Guid saleId,CancellationToken ct)
    {
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);await Establish(db,scope,ct);
        var sale=await db.PosTransactions.AsNoTracking().SingleOrDefaultAsync(t=>t.TenantId==scope.TenantId&&t.Id==saleId,ct)??throw new InventoryPostingNotFoundException();
        await Location(db,scope,sale.StoreId,ct);
        var job=await db.Set<PosReturnFiscalization>().FromSqlInterpolated($"SELECT * FROM public.pos_return_fiscalizations WHERE \"TenantId\"={scope.TenantId} AND \"SaleId\"={saleId} FOR UPDATE").SingleOrDefaultAsync(ct)??throw new InventoryPostingNotFoundException();
        if(job.Status!="pending")return new(job.Id,job.Status,job.Attempts,job.FiscalNumber);
        if(sale.Status!="fiscalized"||string.IsNullOrWhiteSpace(sale.FiscalNumber))return new(job.Id,"pending",job.Attempts,null);
        job.Attempts++;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var fiscal=await fiscalFactory.GetForTenantAsync(scope.TenantId,timeout.Token);FiscalReceiptResult result;
            var original=await fiscal.GetReceiptStatusAsync(saleId.ToString(),timeout.Token);
            if(original.Status!=FiscalReceiptStatus.Done||original.ProviderReceiptId!=saleId.ToString()||original.FiscalNumber!=sale.FiscalNumber||original.TotalAmount!=job.Amount)
            {
                job.LastError="original_provider_receipt_unverified";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return new(job.Id,job.Status,job.Attempts,job.FiscalNumber);
            }
            // Unknown outcomes are resolved by the same provider UUID before any resubmission.
            try {result=await fiscal.GetReceiptStatusAsync(job.Id.ToString(),timeout.Token);}
            catch(FiscalProviderException e) when(e.HttpStatus==404)
            {result=await fiscal.CreateReceiptAsync(JsonSerializer.Deserialize<FiscalReceiptRequest>(job.RequestJson)!,timeout.Token);}
            if(result.Status==FiscalReceiptStatus.Done)
            {
                if(!Guid.TryParse(result.ProviderReceiptId,out var id)||id!=job.Id||string.IsNullOrWhiteSpace(result.FiscalNumber)||result.TotalAmount is not decimal amount||Math.Abs(amount)!=job.Amount)
                {job.Status="failed";job.LastError="provider_receipt_mismatch";}
                else {job.Status="done";job.FiscalNumber=result.FiscalNumber;job.LastError=null;}
            }
            else if(result.Status is FiscalReceiptStatus.Error or FiscalReceiptStatus.Cancelled){job.Status="failed";job.LastError="provider_rejected_reconcile";}
            else job.LastError=null;
        }
        catch(Exception e) when(e is FiscalProviderException or HttpRequestException or OperationCanceledException)
        {job.LastError="provider_outcome_unknown";}
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(job.Id,job.Status,job.Attempts,job.FiscalNumber);
    }
    private static async Task Establish(AppDbContext db,WorkAccessScope scope,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true),set_config('app.erp_posting_id','',true)",ct);
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==scope.UserId&&u.TenantId==scope.TenantId&&u.IsActive&&u.Role==scope.Role,ct);
        var tenant=await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==scope.TenantId&&t.IsActive,ct);
        if(user==null||!InventoryAdjustmentPlanner.CanAdjust(scope.Role)||tenant==null||!tenant.HasModule("inventory")||!tenant.HasModule("pos"))throw new WorkAccessDeniedException();
        if(user.Permissions?.GetValueOrDefault("pos",true)==false&&!await db.UserPermissionGrants.AnyAsync(g=>g.TenantId==scope.TenantId&&g.UserId==scope.UserId&&g.PermissionKey=="pos"&&g.RevokedAt==null&&g.ExpiresAt>DateTime.UtcNow,ct))throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{scope.Role},true)",ct);
    }
    private static async Task Location(AppDbContext db,WorkAccessScope s,Guid id,CancellationToken ct)
    {
        if(!s.CanLocation(id)||!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==id&&l.IsActive,ct)||s.Role!="enterprise_admin"&&!await db.UserLocations.AnyAsync(l=>l.TenantId==s.TenantId&&l.UserId==s.UserId&&l.LocationId==id,ct))throw new WorkAccessDeniedException();
    }
    private static async Task<bool> Ready(AppDbContext db,Guid tenant,Guid location,CancellationToken ct)=>
        await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        && await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{location}) AS \"Value\"").SingleAsync(ct);
    private static async Task<Plan> Load(AppDbContext db,WorkAccessScope scope,Guid saleId,Guid shiftId,bool locked,CancellationToken ct)
    {
        var sale=locked?await db.PosTransactions.FromSqlInterpolated($"SELECT * FROM public.pos_transactions WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={saleId} FOR UPDATE").SingleOrDefaultAsync(ct):await db.PosTransactions.SingleOrDefaultAsync(t=>t.TenantId==scope.TenantId&&t.Id==saleId,ct);
        if(sale==null)throw new InventoryPostingNotFoundException();await Location(db,scope,sale.StoreId,ct);
        var original=await db.BusinessDocuments.SingleOrDefaultAsync(d=>d.TenantId==scope.TenantId&&d.SourceSystem==PosInventoryPostingBridge.Source&&d.SourceId==saleId.ToString(),ct)??throw Conflict("Legacy receipt has no immutable ERP allocation; reconcile separately.");
        if(original.Status!="posted")throw Conflict("Sale has already been reversed.");
        var shift=locked?await db.PosShifts.FromSqlInterpolated($"SELECT * FROM public.pos_shifts WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={shiftId} FOR UPDATE").SingleOrDefaultAsync(ct):await db.PosShifts.SingleOrDefaultAsync(s=>s.TenantId==scope.TenantId&&s.Id==shiftId,ct);
        if(shift==null||shift.ClosedAt!=null||shift.StoreId!=sale.StoreId||shift.RegisterId!=sale.CashRegisterId||shift.CashierId!=scope.UserId)throw Conflict("An open return shift at the original register is required.");
        if(shift.RegisterId is Guid reg&&!await db.CashRegisters.AnyAsync(r=>r.Id==reg&&r.TenantId==scope.TenantId&&r.StoreId==sale.StoreId&&r.Status==CashRegister.StatusActive,ct))throw Conflict("Return register unavailable.");
        var items=await db.PosTransactionItems.Where(i=>i.TransactionId==saleId).ToListAsync(ct);
        if(sale.LoyaltyMembershipId!=null||await db.LoyaltyLedgerEntries.AnyAsync(e=>e.TenantId==scope.TenantId&&e.PosTransactionId==saleId,ct))throw Conflict("Loyalty receipts require an explicit bonus-lot compensation policy.");
        if(sale.PaymentType.ToLowerInvariant() is not ("cash" or "card")||items.Count is <1 or >100||items.Any(i=>i.ProductNameSnapshot==null||i.LineTotal!=decimal.Round(i.PriceFinal*i.Quantity,2,MidpointRounding.AwayFromZero))||items.Sum(i=>i.LineTotal)!=sale.TotalAmount)throw Conflict("Unsupported original financial snapshot; reconciliation required.");
        var posting=await db.DocumentPostings.SingleAsync(p=>p.TenantId==scope.TenantId&&p.DocumentId==original.Id,ct);
        var entries=await db.InventoryLedgerEntries.Where(e=>e.TenantId==scope.TenantId&&e.PostingId==posting.Id).OrderBy(e=>e.EntryNumber).ToListAsync(ct);
        if(entries.Count is <1 or >100)throw Conflict("Invalid original allocations.");
        var rows=new List<Row>();
        foreach(var e in entries)
        {
            var line=await db.DocumentLines.SingleAsync(l=>l.TenantId==scope.TenantId&&l.Id==e.DocumentLineId,ct);
            var item=locked?await db.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={e.ItemId} FOR SHARE").SingleAsync(ct)
                :await db.Items.AsNoTracking().SingleAsync(i=>i.TenantId==scope.TenantId&&i.Id==e.ItemId,ct);
            if(item.Unit!=line.Unit)throw Conflict("The original inventory unit changed; reconcile before returning stock.");
            var stock=await db.ProductStocks.SingleAsync(s=>s.Id==e.BatchId&&s.TenantId==scope.TenantId,ct);
            var balance=await db.InventoryBalanceProjections.SingleAsync(b=>b.TenantId==scope.TenantId&&b.BatchId==stock.Id,ct);
            if(e.QuantityDelta>=0||stock.StoreId!=e.LocationId||stock.ProductId!=e.ItemId||stock.ZoneId!=e.ZoneId||stock.BatchNumber!=e.BatchNumberSnapshot||stock.ExpiryDate!=e.ExpiryDateSnapshot||stock.Status=="archived"
                ||!TransferPostingRules.Exact(stock.Quantity-e.QuantityDelta)||balance.Quantity!=stock.Quantity||balance.ReservedQuantity!=0||await db.InventoryLedgerEntries.Where(l=>l.TenantId==scope.TenantId&&l.BatchId==stock.Id).SumAsync(l=>l.QuantityDelta,ct)!=stock.Quantity)throw Conflict("Return batch requires reconciliation.");
            if(await db.InventoryReservations.AnyAsync(r=>r.TenantId==scope.TenantId&&r.LocationId==stock.StoreId&&r.ItemId==stock.ProductId&&r.Status=="active",ct))throw Conflict("Reserved batch requires reconciliation.");
            rows.Add(new(e,line,stock,balance));
        }
        var snapshot=await db.Database.SqlQuery<string>($"SELECT public.erp_w4_pos_snapshot({saleId})::text AS \"Value\"").SingleAsync(ct);
        var fingerprint=ReceiptPostingRules.Hash(new{saleId,shiftId,snapshot,original=posting.Id,rows=rows.Select(r=>new{r.Original.Id,r.Stock.Quantity,r.Stock.Status,r.Stock.ZoneId,r.Stock.BatchNumber,r.Stock.ExpiryDate,r.Balance.LastLedgerEntryId,version=db.Entry(r.Stock).Property<uint>("xmin").CurrentValue})});
        return new(sale,original,posting,shift,rows,snapshot,fingerprint);
    }
    private static InventoryPostingConflictException Conflict(string reason)=>new(reason);
}
