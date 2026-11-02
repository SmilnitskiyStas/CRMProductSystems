using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ProductionPostingStore(DbContextOptions<AppDbContext> options,IInventoryPostingGate gate,
    ProductionPostingOptions enabled,TimeProvider clock,ProductionPostingPreviewReader reader):IProductionPostingStore
{
    public const string Source="production.complete.v1";
    public async Task<ProductionPostingResult> CompleteAsync(WorkAccessScope scope,Guid orderId,ProductionPostingCommand command,CancellationToken ct)
    {
        if(orderId==Guid.Empty||command.RequestId==Guid.Empty)throw new ArgumentException("Order and request required.");
        TransferPostingRules.Fingerprint(command.ExpectedFingerprint);command=command with{Reason=WriteOffPostingRules.Reason("reverse",command.Reason)!};
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(1));ct=deadline.Token;
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'",ct);
            await ProductionPostingPreviewReader.Establish(db,scope,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id','',true),set_config('app.erp_review_document_id','',true)",ct);
            var location=await db.ProductionOrders.AsNoTracking().Where(o=>o.TenantId==scope.TenantId&&o.Id==orderId).Select(o=>(Guid?)o.LocationId).SingleOrDefaultAsync(ct)??throw new InventoryPostingNotFoundException();
            if(!scope.CanLocation(location)||!await db.Locations.AnyAsync(l=>l.TenantId==scope.TenantId&&l.Id==location&&l.IsActive,ct)
                ||scope.Role!="enterprise_admin"&&!await db.UserLocations.AnyAsync(l=>l.TenantId==scope.TenantId&&l.UserId==scope.UserId&&l.LocationId==location,ct))throw new WorkAccessDeniedException();
            if(!enabled.Enabled||!gate.Allows(scope.TenantId,location))throw new InventoryPostingDisabledException();
            if(!await ProductionPostingPreviewReader.Ready(db,scope.TenantId,location,ct))throw Conflict("Production scope migration required.");
            // First cut serialises source writers as well as FEFO phantoms. Never activates a scope.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.product_stock IN EXCLUSIVE MODE; LOCK TABLE public.inventory_reservations IN SHARE MODE; LOCK TABLE public.production_orders IN SHARE ROW EXCLUSIVE MODE; LOCK TABLE public.recipes,public.recipe_ingredients,public.items,public.production_order_request_lines,public.production_request_lines,public.production_requests IN SHARE MODE",ct);
            await ProductionPostingPreviewReader.Establish(db,scope,ct);
            var hash=ReceiptPostingRules.Hash(new{orderId,command});var key=$"{Source}:{command.RequestId:N}";
            var prior=await db.BusinessDocuments.AsNoTracking().SingleOrDefaultAsync(d=>d.TenantId==scope.TenantId&&d.SourceSystem==Source&&d.SourceId==orderId.ToString(),ct);
            if(prior!=null)
            {
                if(prior.Status!="posted")throw Conflict("Incomplete production history.");
                var posting=await db.DocumentPostings.AsNoTracking().SingleAsync(p=>p.TenantId==scope.TenantId&&p.DocumentId==prior.Id,ct);
                if(posting.RequestHash!=hash||posting.IdempotencyKey!=key)throw Conflict("Different production confirmation.");
                var output=await db.ProductStocks.AsNoTracking().SingleAsync(b=>b.TenantId==scope.TenantId&&b.SourceType=="production"&&b.SourceId==orderId,ct);
                var priorMessage=await db.OutboxEvents.AsNoTracking().SingleAsync(o=>o.TenantId==scope.TenantId&&o.PostingId==posting.Id,ct);
                ErpEventContracts.FromOutbox(priorMessage);await tx.CommitAsync(ct);return new(orderId,prior.Id,posting.Id,output.Id,priorMessage.Id,true);
            }
            if(await db.DocumentPostings.AnyAsync(p=>p.TenantId==scope.TenantId&&p.IdempotencyKey==key,ct))throw Conflict("Request identity already used for another order.");
            var plan=await reader.ReadInContextAsync(db,scope,orderId,ct);
            if(!plan.CanPost||plan.Fingerprint!=command.ExpectedFingerprint)throw Conflict("Production plan changed or requires reconciliation.");
            if(await db.ProductionOrderConsumptions.AnyAsync(c=>c.ProductionOrderId==orderId,ct)||await db.ProductStocks.AnyAsync(b=>b.TenantId==scope.TenantId&&b.SourceType=="production"&&b.SourceId==orderId,ct))throw Conflict("Legacy production facts require reconciliation.");
            var time=clock.GetUtcNow().UtcDateTime;var at=new DateTime(time.Ticks-time.Ticks%10,DateTimeKind.Utc);
            if(DateOnly.FromDateTime(at)!=plan.Source.ProductionDate)throw Conflict("Production date changed; review again.");
            var order=await db.ProductionOrders.SingleAsync(o=>o.TenantId==scope.TenantId&&o.Id==orderId,ct);
            var source=await db.Database.SqlQuery<string>($"SELECT public.erp_w4_production_snapshot({orderId})::text AS \"Value\"").SingleAsync(ct);
            using var sourceJson=JsonDocument.Parse(source);
            var doc=new BusinessDocument{TenantId=scope.TenantId,DocumentType="production_completion",SourceSystem=Source,SourceId=orderId.ToString(),CreatedBy=scope.UserId,
                Number="PROD:"+orderId.ToString("N"),ContentHash=hash,CreatedAt=at,UpdatedAt=at,OccurredAt=at};
            db.Add(doc);await db.SaveChangesAsync(ct);
            var postingNew=new DocumentPosting{TenantId=scope.TenantId,DocumentId=doc.Id,PerformedBy=scope.UserId,IdempotencyKey=key,RequestHash=hash,PostedAt=at};
            var outputStock=new ProductStock{TenantId=scope.TenantId,ProductId=plan.Output.ItemId,StoreId=location,Quantity=plan.Output.Quantity,QuantityInitial=plan.Output.Quantity,
                ExpiryDate=plan.Output.ExpiryDate!.Value,BatchNumber=plan.Output.BatchNumber,SourceType="production",SourceId=orderId,AddedBy=scope.UserId,AddedAt=at,LastCheckedAt=at,
                Status=StockStatus.Compute(plan.Output.Quantity,plan.Output.ExpiryDate.Value,at)};
            var outputEvent=new StockEvent{TenantId=scope.TenantId,ProductStockId=outputStock.Id,ProductId=outputStock.ProductId,StoreId=location,EventType="production_output",QuantityDelta=outputStock.Quantity,
                PerformedBy=scope.UserId,CreatedAt=at,Meta=JsonSerializer.Serialize(new{production_order_id=orderId,batch_number=outputStock.BatchNumber})};
            var lines=new List<DocumentLine>();var entries=new List<InventoryLedgerEntry>();var sourceEvents=new List<StockEvent>();var consumptions=new List<ProductionOrderConsumption>();
            foreach(var take in plan.Materials)
            {
                var batch=plan.Source.Batches.Single(b=>b.Id==take.BatchId);var item=plan.Source.Ingredients.Single(i=>i.Id==take.IngredientId).Item!;
                var consumption=new ProductionOrderConsumption{ProductionOrderId=orderId,ItemId=take.ItemId,ProductStockId=take.BatchId,QtyConsumed=take.Quantity,ConsumedAt=new(at)};
                var ev=new StockEvent{TenantId=scope.TenantId,EventType="production_consumption",ProductStockId=take.BatchId,ProductId=take.ItemId,StoreId=location,QuantityDelta=-take.Quantity,
                    PerformedBy=scope.UserId,CreatedAt=at,Meta=JsonSerializer.Serialize(new{production_order_id=orderId})};
                var line=new DocumentLine{TenantId=scope.TenantId,DocumentId=doc.Id,LineNumber=lines.Count+1,ItemId=take.ItemId,FromLocationId=location,Quantity=take.Quantity,Unit=take.Unit,
                    UnitCost=take.UnitCost,ItemNameSnapshot=item.Name,BarcodeSnapshot=item.Barcode,BatchNumberSnapshot=batch.BatchNumber,ExpiryDateSnapshot=batch.ExpiryDate,
                    Metadata=JsonSerializer.Serialize(new{role="material",batchId=take.BatchId,ingredientId=take.IngredientId,eventId=ev.Id,consumptionId=consumption.Id,beforeStatus=batch.Status,lastLedgerEntryId=take.LastLedgerEntryId})};
                lines.Add(line);consumptions.Add(consumption);sourceEvents.Add(ev);
                entries.Add(new(){TenantId=scope.TenantId,DocumentId=doc.Id,PostingId=postingNew.Id,DocumentLineId=line.Id,EntryNumber=entries.Count+1,ItemId=take.ItemId,LocationId=location,BatchId=take.BatchId,
                    ZoneId=batch.ZoneId,QuantityBefore=take.QuantityBefore,QuantityDelta=-take.Quantity,QuantityAfter=take.QuantityAfter,UnitCost=take.UnitCost,ValueDelta=-take.Value,
                    BatchNumberSnapshot=batch.BatchNumber,ExpiryDateSnapshot=batch.ExpiryDate,CreatedAt=at,OccurredAt=at});
            }
            var outputLine=new DocumentLine{TenantId=scope.TenantId,DocumentId=doc.Id,LineNumber=lines.Count+1,ItemId=plan.Output.ItemId,ToLocationId=location,Quantity=plan.Output.Quantity,
                Unit=plan.Output.Unit,UnitCost=plan.Output.UnitCost,ItemNameSnapshot=plan.Output.Name!,BarcodeSnapshot=plan.Source.OutputItem?.Barcode,BatchNumberSnapshot=outputStock.BatchNumber,ExpiryDateSnapshot=outputStock.ExpiryDate,
                Metadata=JsonSerializer.Serialize(new{role="output",batchId=outputStock.Id,eventId=outputEvent.Id,source=sourceJson.RootElement,review=plan.Source})};
            lines.Add(outputLine);entries.Add(new(){TenantId=scope.TenantId,DocumentId=doc.Id,DocumentLineId=outputLine.Id,PostingId=postingNew.Id,EntryNumber=entries.Count+1,ItemId=outputStock.ProductId,LocationId=location,
                BatchId=outputStock.Id,QuantityBefore=0,QuantityDelta=outputStock.Quantity,QuantityAfter=outputStock.Quantity,UnitCost=plan.Output.UnitCost,ValueDelta=plan.Output.Value,
                BatchNumberSnapshot=outputStock.BatchNumber,ExpiryDateSnapshot=outputStock.ExpiryDate,CreatedAt=at,OccurredAt=at});
            db.AddRange(lines);await db.SaveChangesAsync(ct);doc.Status="submitted";await db.SaveChangesAsync(ct);
            db.Add(new DocumentApproval{TenantId=scope.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=scope.UserId,Decision="approved",ContentHash=hash,Reason=command.Reason,CreatedAt=at});
            doc.Status="approved";await db.SaveChangesAsync(ct);db.Add(postingNew);await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{postingNew.Id.ToString()},true)",ct);
            db.Add(outputStock);await db.SaveChangesAsync(ct);db.AddRange(entries);await db.SaveChangesAsync(ct);
            for(var i=0;i<plan.Materials.Count;i++)
            {
                var take=plan.Materials[i];var entry=entries[i];var stock=await db.ProductStocks.SingleAsync(b=>b.TenantId==scope.TenantId&&b.Id==take.BatchId,ct);
                stock.Quantity=take.QuantityAfter;stock.Status=StockStatus.Recalculate(stock);
                // The immediate fence verifies the previous projection before it is advanced.
                await db.SaveChangesAsync(ct);
                var balance=await db.InventoryBalanceProjections.SingleAsync(b=>b.TenantId==scope.TenantId&&b.BatchId==stock.Id,ct);balance.Quantity=stock.Quantity;balance.LastLedgerEntryId=entry.Id;balance.UpdatedAt=at;
                db.Add(new StockMovement{TenantId=scope.TenantId,ProductId=stock.ProductId,ProductStockId=stock.Id,FromStoreId=location,FromZoneId=stock.ZoneId,MovementType="write_off",Quantity=take.Quantity,
                    QuantityBefore=take.QuantityBefore,QuantityAfter=take.QuantityAfter,ReferenceType="production_consumption_event",ReferenceId=sourceEvents[i].Id,PerformedBy=scope.UserId,CreatedAt=at});
            }
            db.Add(new InventoryBalanceProjection{TenantId=scope.TenantId,ItemId=outputStock.ProductId,LocationId=location,BatchId=outputStock.Id,Quantity=outputStock.Quantity,ReservedQuantity=0,LastLedgerEntryId=entries[^1].Id,UpdatedAt=at});
            db.Add(new StockMovement{TenantId=scope.TenantId,ProductId=outputStock.ProductId,ProductStockId=outputStock.Id,ToStoreId=location,MovementType="receipt",Quantity=outputStock.Quantity,
                QuantityBefore=0,QuantityAfter=outputStock.Quantity,ReferenceType="production_output",ReferenceId=orderId,PerformedBy=scope.UserId,CreatedAt=at});
            db.AddRange(consumptions);db.AddRange(sourceEvents);db.Add(outputEvent);
            var message=ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(scope.TenantId,Guid.NewGuid(),new(at),new(doc.Id,doc.DocumentKey,doc.Version,postingNew.Id,
                entries.Select(e=>new InventoryLedgerDeltaV1(e.Id,e.EntryNumber,e.ItemId,e.LocationId,e.BatchId,e.QuantityDelta,e.UnitCost,e.ValueDelta,e.Currency,e.ZoneId)).ToArray()),correlationId:orderId));
            db.Add(message);order.Status=ProductionOrderStatus.Done;order.CompletedAt=new(at);order.UpdatedAt=new(at);await db.SaveChangesAsync(ct);
            await NotifyReady(db,scope,plan,orderId,at,ct);
            doc.Status="posted";doc.PostedAt=at;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(orderId,doc.Id,postingNew.Id,outputStock.Id,message.Id,false);
        }
        catch(Exception e) when(e is DbUpdateConcurrencyException||(e as PostgresException??e.InnerException as PostgresException)?.SqlState is "55P03" or "40001" or "40P01" or "23505" or "P4F01")
        {throw Conflict("Concurrent or inconsistent production state; retry the same confirmation after review.");}
    }
    private static async Task NotifyReady(AppDbContext db,WorkAccessScope scope,ProductionPostingPreview plan,Guid orderId,DateTime at,CancellationToken ct)
    {
        var requestIds=plan.Source.Requests.Where(r=>r.InScope&&r.RequestId.HasValue).Select(r=>r.RequestId!.Value).Distinct().ToArray();
        var requests=await db.ProductionRequests.Where(r=>r.TenantId==scope.TenantId&&requestIds.Contains(r.Id)).ToListAsync(ct);
        foreach(var request in requests)
        {
            var lines=await db.ProductionRequestLines.Where(l=>l.TenantId==scope.TenantId&&l.RequestId==request.Id).ToListAsync(ct);var ids=lines.Select(l=>l.Id).ToArray();
            var links=await db.ProductionOrderRequestLines.Where(l=>l.TenantId==scope.TenantId&&ids.Contains(l.RequestLineId)).ToListAsync(ct);var orderIds=links.Select(l=>l.ProductionOrderId).ToArray();
            var orders=await db.ProductionOrders.Where(o=>o.TenantId==scope.TenantId&&orderIds.Contains(o.Id)).ToDictionaryAsync(o=>o.Id,ct);
            // Both sets are tracked: EF fixes up the init-only order navigation.
            foreach(var link in links){if(!orders.TryGetValue(link.ProductionOrderId,out var order)||order.LocationId!=request.ProductionLocationId)throw Conflict("Request allocation crosses production scope.");}
            foreach(var line in lines)
            {
                var active=links.Where(l=>l.RequestLineId==line.Id&&l.ProductionOrder!.Status!=ProductionOrderStatus.Cancelled).ToArray();
                if(active.Any(l=>l.Qty<=0)||active.Sum(l=>l.Qty)>(line.AcceptedQty??0))throw Conflict("Request allocation exceeds accepted quantity.");
            }
            if(ProductionRequestStages.Derive(request.Status,lines,links)!=ProductionRequestStage.Ready)continue;
            if(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM public.notification_queue WHERE \"TenantId\"={scope.TenantId} AND \"EventType\"='production_request.ready' AND \"Payload\"->>'requestId'={request.Id.ToString()}) AS \"Value\"").SingleAsync(ct))continue;
            db.Add(new NotificationQueue{TenantId=scope.TenantId,StoreId=request.StoreLocationId,EventType=ProductionOrderSyncService.ReadyEventType,Channel="system",Status="pending",CreatedAt=at,
                Title="Виробництво завершено",Payload=JsonSerializer.Serialize(new{requestId=request.Id,storeLocationId=request.StoreLocationId,productionLocationId=request.ProductionLocationId,neededBy=request.NeededBy.ToString("yyyy-MM-dd"),orderId})});
        }
        await db.SaveChangesAsync(ct);
    }
    private static InventoryPostingConflictException Conflict(string message)=>new(message);
}
