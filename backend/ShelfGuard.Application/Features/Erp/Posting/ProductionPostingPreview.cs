using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record ProductionItemReview(Guid Id,string Name,string Unit,string? Barcode,bool Active,int? ShelfLifeDays);
public sealed record ProductionIngredientReview(Guid Id,Guid ItemId,decimal Quantity,string Unit,ProductionItemReview? Item);
public sealed record ProductionRecipeReview(Guid Id,string Name,Guid OutputItemId,decimal OutputQuantity,string Unit,bool Active,DateTimeOffset UpdatedAt);
public sealed record ProductionOrderReview(Guid Id,Guid TenantId,Guid LocationId,Guid RecipeId,decimal PlannedQuantity,string Status,DateTimeOffset UpdatedAt,DateTimeOffset? StartedAt);
public sealed record ProductionRequestReview(Guid Id,Guid RequestLineId,decimal Quantity,Guid? RequestId,string? Status,bool InScope,
    decimal? RequestedQuantity=null,decimal? AcceptedQuantity=null,decimal? AllocatedQuantity=null,DateTime? UpdatedAt=null);
public sealed record ProductionBatchReview(Guid Id,Guid ItemId,Guid LocationId,Guid? ZoneId,decimal Quantity,DateOnly ExpiryDate,DateTime AddedAt,
    string Status,string? BatchNumber,uint Version,bool Reconciled,Guid? LastLedgerEntryId,decimal? UnitCost,string? LedgerUnit);
public sealed record ProductionReviewSource(ProductionOrderReview Order,ProductionRecipeReview Recipe,ProductionItemReview? OutputItem,
    IReadOnlyList<ProductionIngredientReview> Ingredients,IReadOnlyList<ProductionBatchReview> Batches,IReadOnlyList<Guid> ReservedItemIds,
    IReadOnlyList<ProductionRequestReview> Requests,DateOnly ProductionDate);
public sealed record ProductionReviewIssue(string Code,Guid? ItemId=null,Guid? BatchId=null,decimal? Required=null,decimal? Available=null);
public sealed record ProductionMaterialTake(Guid IngredientId,Guid ItemId,Guid BatchId,string Unit,decimal Quantity,decimal QuantityBefore,decimal QuantityAfter,
    decimal? UnitCost,decimal? Value,Guid? LastLedgerEntryId);
public sealed record ProductionOutputReview(Guid ItemId,string? Name,string Unit,decimal Quantity,DateOnly? ExpiryDate,string BatchNumber,
    decimal? MaterialCost,decimal? UnitCost,decimal? Value,decimal? ValuationDifference);
public sealed record ProductionPostingPreview(string Protocol,string Fingerprint,bool Configured,bool Fenced,bool Reviewable,
    IReadOnlyList<ProductionMaterialTake> Materials,ProductionOutputReview Output,IReadOnlyList<ProductionReviewIssue> Issues,
    IReadOnlyList<ProductionReviewIssue> Warnings,ProductionReviewSource Source)
{
    public bool WriterImplemented { get; init; }
    public bool Ready { get; init; }
    public bool CanPost => WriterImplemented&&Ready&&Configured&&Fenced&&Reviewable;
}
public sealed record ProductionPostingCommand(Guid RequestId,string ExpectedFingerprint,string Reason);
public sealed record ProductionPostingResult(Guid OrderId,Guid DocumentId,Guid PostingId,Guid OutputBatchId,Guid EventId,bool Replayed);
public interface IProductionPosting { Task<ProductionPostingResult> CompleteAsync(Guid orderId,ProductionPostingCommand command,CancellationToken ct=default); }
public interface IProductionPostingStore { Task<ProductionPostingResult> CompleteAsync(WorkAccessScope scope,Guid orderId,ProductionPostingCommand command,CancellationToken ct); }
public sealed class ProductionPostingService(IWorkAccessContext access,IProductionPostingStore store):IProductionPosting
{
    public async Task<ProductionPostingResult> CompleteAsync(Guid orderId,ProductionPostingCommand command,CancellationToken ct=default)
    {
        if(orderId==Guid.Empty||command.RequestId==Guid.Empty)throw new ArgumentException("Order and durable request identity required.");
        TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
        command=command with{Reason=WriteOffPostingRules.Reason("reverse",command.Reason)!};
        var scope=await access.GetAsync(ct)??throw new WorkAccessDeniedException();
        if(!scope.Has("production.complete"))throw new WorkAccessDeniedException();
        return await store.CompleteAsync(scope,orderId,command,ct);
    }
}
public sealed record ProductionPostingOptions(bool Enabled);
public interface IProductionPostingPreview { Task<ProductionPostingPreview> PreviewAsync(Guid orderId,CancellationToken ct=default); }
public interface IProductionPostingPreviewReader { Task<ProductionPostingPreview> ReadAsync(WorkAccessScope scope,Guid orderId,CancellationToken ct); }
public sealed class ProductionPostingPreviewService(IWorkAccessContext access,IProductionPostingPreviewReader reader) : IProductionPostingPreview
{
    public async Task<ProductionPostingPreview> PreviewAsync(Guid orderId,CancellationToken ct=default)
    {
        if(orderId==Guid.Empty)throw new ArgumentException("Production order identity required.");
        var scope=await access.GetAsync(ct)??throw new WorkAccessDeniedException();
        if(!scope.Has("production.complete"))throw new WorkAccessDeniedException();
        return await reader.ReadAsync(scope,orderId,ct);
    }
}

/// <summary>Exact, bounded bill of materials and FEFO review. Does not mutate source objects.</summary>
public static class ProductionPostingPlanner
{
    public const string Protocol="production.review.v1";
    public const decimal MaximumValue=99_999_999_999_999.9999m;
    public static ProductionPostingPreview Plan(ProductionReviewSource input,bool configured,bool fenced)
    {
        var source=input with{Ingredients=input.Ingredients.OrderBy(i=>i.Id).ToArray(),Batches=input.Batches.OrderBy(b=>b.Id).ToArray(),
            ReservedItemIds=input.ReservedItemIds.Distinct().OrderBy(id=>id).ToArray(),Requests=input.Requests.OrderBy(r=>r.Id).ToArray()};
        if(source.Order.Id==Guid.Empty||source.Order.TenantId==Guid.Empty||source.Order.LocationId==Guid.Empty||source.Order.RecipeId!=source.Recipe.Id
            ||source.Ingredients.Select(i=>i.Id).Distinct().Count()!=source.Ingredients.Count||source.Batches.Select(b=>b.Id).Distinct().Count()!=source.Batches.Count)
            throw new ArgumentException("Inconsistent production source identities.");
        var issues=new List<ProductionReviewIssue>();var warnings=new List<ProductionReviewIssue>();var takes=new List<ProductionMaterialTake>();
        void Issue(string code,Guid? item=null,Guid? batch=null,decimal? required=null,decimal? available=null)=>issues.Add(new(code,item,batch,required,available));
        if(source.Order.Status!="in_progress"||source.Order.StartedAt==null)Issue("order_not_in_progress");
        if(!source.Recipe.Active)Issue("recipe_inactive");
        var output=source.OutputItem;
        if(output==null||output.Id!=source.Recipe.OutputItemId||!output.Active||!Text(output.Name,500))Issue("output_item_unavailable",source.Recipe.OutputItemId);
        if(!Text(source.Recipe.Unit,24)||output?.Unit!=source.Recipe.Unit)Issue("output_unit_mismatch",source.Recipe.OutputItemId);
        if(source.ReservedItemIds.Contains(source.Recipe.OutputItemId))Issue("output_reservation_requires_policy",source.Recipe.OutputItemId);
        var outputExact=TransferPostingRules.Exact(source.Order.PlannedQuantity)&&source.Order.PlannedQuantity>0;
        if(!outputExact)Issue("output_quantity_not_representable",source.Recipe.OutputItemId);
        var recipeExact=source.Recipe.OutputQuantity>0&&source.Recipe.OutputQuantity<=9_999_999.999m&&decimal.Round(source.Recipe.OutputQuantity,3)==source.Recipe.OutputQuantity;
        if(!recipeExact)Issue("invalid_recipe_yield");
        DateOnly? expiry=null;
        if(output?.ShelfLifeDays is not >0)Issue("output_shelf_life_required",source.Recipe.OutputItemId);
        else try{expiry=source.ProductionDate.AddDays(output.ShelfLifeDays.Value);}catch(ArgumentOutOfRangeException){Issue("output_shelf_life_out_of_range",output.Id);}
        if(source.Ingredients.Count is <1 or >99)Issue("ingredient_count_out_of_range");
        if(source.Batches.Count>1000)Issue("candidate_batch_limit_exceeded");
        if(source.Ingredients.Select(i=>i.ItemId).Distinct().Count()!=source.Ingredients.Count)Issue("duplicate_recipe_item");
        if(source.Requests.Any(r=>!r.InScope||r.Quantity<=0||decimal.Round(r.Quantity,3)!=r.Quantity||r.Status is not ("accepted" or "partial")
            ||r.RequestedQuantity is not >0||r.AcceptedQuantity is not >0||r.AcceptedQuantity>r.RequestedQuantity||r.AllocatedQuantity==null||r.AllocatedQuantity<r.Quantity||r.AllocatedQuantity>r.AcceptedQuantity)
            ||source.Requests.Sum(r=>r.Quantity)>source.Order.PlannedQuantity)
            Issue("linked_requests_require_reconciliation");
        // Reject invalid source math before division. Nothing is rounded into a physical stock quantity.
        if(outputExact&&recipeExact&&source.Ingredients.Count is >=1 and <=99&&source.Batches.Count<=1000
            &&source.Ingredients.Select(i=>i.ItemId).Distinct().Count()==source.Ingredients.Count)
        foreach(var ingredient in source.Ingredients.OrderBy(i=>i.ItemId))
        {
            var item=ingredient.Item;
            if(item==null||item.Id!=ingredient.ItemId||!item.Active||!Text(item.Name,500)){Issue("ingredient_item_unavailable",ingredient.ItemId);continue;}
            if(!Text(ingredient.Unit,24)||ingredient.Unit!=item.Unit){Issue("ingredient_unit_mismatch",item.Id);continue;}
            if(item.Id==source.Recipe.OutputItemId){Issue("self_consumption_requires_policy",item.Id);continue;}
            if(ingredient.Quantity<=0||ingredient.Quantity>9_999_999.999m||decimal.Round(ingredient.Quantity,3)!=ingredient.Quantity){Issue("invalid_ingredient_quantity",item.Id);continue;}
            var numerator=ingredient.Quantity*source.Order.PlannedQuantity;
            var required=numerator/source.Recipe.OutputQuantity;
            if(!TransferPostingRules.Exact(required)||required<=0||required*source.Recipe.OutputQuantity!=numerator){Issue("ingredient_quantity_not_representable",item.Id,required:required);continue;}
            if(source.ReservedItemIds.Contains(item.Id))Issue("active_reservation_requires_policy",item.Id);
            var candidates=source.Batches.Where(b=>b.ItemId==item.Id&&b.LocationId==source.Order.LocationId&&b.Quantity>0&&b.ExpiryDate>source.ProductionDate&&b.Status is not ("archived" or "sold_out" or "expired" or "temp_violation"))
                .OrderBy(b=>b.ExpiryDate).ThenBy(b=>b.AddedAt).ThenBy(b=>b.Id).ToArray();
            var remaining=required;
            foreach(var batch in candidates)
            {
                if(remaining==0)break;
                if(!TransferPostingRules.Exact(batch.Quantity)){Issue("batch_quantity_not_representable",item.Id,batch.Id);continue;}
                var quantity=Math.Min(remaining,batch.Quantity);remaining-=quantity;
                if(!batch.Reconciled||batch.LastLedgerEntryId==null)Issue("inventory_not_reconciled",item.Id,batch.Id);
                if(batch.LedgerUnit!=item.Unit)Issue("ledger_unit_mismatch",item.Id,batch.Id);
                decimal? value=null;
                if(batch.UnitCost is decimal cost)
                {
                    if(cost<0||cost>MaximumValue||decimal.Round(cost,4)!=cost)Issue("invalid_ledger_cost",item.Id,batch.Id);
                    else {value=decimal.Round(quantity*cost,4,MidpointRounding.AwayFromZero);if(value>MaximumValue)Issue("material_value_out_of_range",item.Id,batch.Id);}
                }
                else warnings.Add(new("unknown_ledger_cost",item.Id,batch.Id));
                takes.Add(new(ingredient.Id,item.Id,batch.Id,item.Unit,quantity,batch.Quantity,batch.Quantity-quantity,batch.UnitCost,value,batch.LastLedgerEntryId));
            }
            if(remaining>0)Issue("insufficient_usable_stock",item.Id,required:required,available:required-remaining);
        }
        if(takes.Count>99)Issue("allocation_limit_exceeded");
        decimal? total=null,unitCost=null,outputValue=null,variance=null;
        if(issues.Count==0&&takes.Count>0&&takes.All(t=>t.Value.HasValue))
        {
            total=takes.Sum(t=>t.Value!.Value);
            if(total>MaximumValue)Issue("material_total_out_of_range");
            else if(outputExact)
            {
                unitCost=decimal.Round(total.Value/source.Order.PlannedQuantity,4,MidpointRounding.AwayFromZero);
                if(unitCost>MaximumValue)Issue("output_cost_out_of_range");
                else {outputValue=decimal.Round(source.Order.PlannedQuantity*unitCost.Value,4,MidpointRounding.AwayFromZero);variance=outputValue-total;
                    if(variance!=0)Issue("valuation_rounding_requires_policy",source.Recipe.OutputItemId);}
            }
        }
        var outPlan=new ProductionOutputReview(source.Recipe.OutputItemId,output?.Name,source.Recipe.Unit,source.Order.PlannedQuantity,expiry,
            $"PROD-{source.Order.Id.ToString("N")[..8].ToUpperInvariant()}-{source.ProductionDate.ToString("yyyyMMdd",CultureInfo.InvariantCulture)}",total,unitCost,outputValue,variance);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{protocol=Protocol,source,materials=takes,output=outPlan,issues,warnings},Json);
        var fingerprint=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(Protocol,fingerprint,configured,fenced,issues.Count==0,takes,outPlan,issues,warnings,source);
    }
    private static bool Text(string? text,int maximum)=>!string.IsNullOrWhiteSpace(text)&&text.Length<=maximum;
    private static readonly JsonSerializerOptions Json=new(){Converters={new CanonicalDecimal()}};
    private sealed class CanonicalDecimal:JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer,decimal value,JsonSerializerOptions options)=>writer.WriteRawValue(value.ToString("G29",CultureInfo.InvariantCulture));
    }
}
