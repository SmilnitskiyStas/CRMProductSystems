using System.Globalization;
using ShelfGuard.Application.Features.Erp.Posting;
using Xunit;
namespace ShelfGuard.Tests.Erp;

public sealed class ProductionPostingPlannerTests
{
    private static readonly DateOnly Today=new(2026,10,27);
    private static readonly DateTimeOffset At=new(2026,10,27,12,0,0,TimeSpan.Zero);
    internal static ProductionReviewSource Source()
    {
        var tenant=Guid.NewGuid();var location=Guid.NewGuid();var order=Guid.NewGuid();var recipe=Guid.NewGuid();var item=Guid.NewGuid();var output=Guid.NewGuid();
        return new(new(order,tenant,location,recipe,2,"in_progress",At,At),new(recipe,"Synthetic recipe",output,2,"kg",true,At),
            new(output,"Output","kg",null,true,5),[new(Guid.NewGuid(),item,3,"kg",new(item,"Ingredient","kg","I-1",true,null))],
            [new(Guid.Parse("00000000-0000-0000-0000-000000000002"),item,location,null,2,Today.AddDays(1),At.UtcDateTime,"safe","early",1,true,Guid.NewGuid(),2,"kg"),
             new(Guid.Parse("00000000-0000-0000-0000-000000000001"),item,location,null,5,Today.AddDays(2),At.UtcDateTime,"safe","late",2,true,Guid.NewGuid(),2,"kg")],[],[],Today);
    }
    [Fact]
    public void Review_has_exact_FEFO_deltas_cost_and_output_but_never_enables_a_writer()
    {
        var s=Source();var p=ProductionPostingPlanner.Plan(s,true,true);
        Assert.True(p.Reviewable);Assert.True(p.Configured);Assert.True(p.Fenced);Assert.False(p.CanPost);Assert.False(p.WriterImplemented);
        Assert.Equal(new decimal[]{2,1},p.Materials.Select(t=>t.Quantity));Assert.Equal(s.Batches[0].Id,p.Materials[0].BatchId);
        Assert.Equal(6,p.Output.MaterialCost);Assert.Equal(3,p.Output.UnitCost);Assert.Equal(6,p.Output.Value);Assert.Equal(0,p.Output.ValuationDifference);
        Assert.Equal(Today.AddDays(5),p.Output.ExpiryDate);Assert.Equal(2,s.Batches[0].Quantity);Assert.Equal(5,s.Batches[1].Quantity);
    }
    [Theory][InlineData("expired")][InlineData("expired_status")][InlineData("archived")][InlineData("sold_out")]
    public void Ineligible_first_batch_is_not_consumed(string reason)
    {
        var s=Source();var first=s.Batches[0] with{ExpiryDate=reason=="expired"?Today:s.Batches[0].ExpiryDate,Status=reason=="expired"?"safe":reason=="expired_status"?"expired":reason};
        var p=ProductionPostingPlanner.Plan(s with{Batches=[first,s.Batches[1]]},false,false);
        Assert.True(p.Reviewable);Assert.Single(p.Materials);Assert.Equal(s.Batches[1].Id,p.Materials[0].BatchId);Assert.Equal(3,p.Materials[0].Quantity);
    }
    [Theory][InlineData("output")][InlineData("scaled")][InlineData("recurring")]
    public void Stock_precision_is_never_silently_rounded(string kind)
    {
        var s=Source();
        if(kind=="output")s=s with{Order=s.Order with{PlannedQuantity=1.001m}};
        if(kind=="scaled")s=s with{Ingredients=[s.Ingredients[0] with{Quantity=0.001m}]};
        if(kind=="recurring")s=s with{Recipe=s.Recipe with{OutputQuantity=3},Ingredients=[s.Ingredients[0] with{Quantity=1}]};
        var p=ProductionPostingPlanner.Plan(s,true,true);Assert.False(p.Reviewable);Assert.Contains(p.Issues,i=>i.Code.EndsWith("not_representable"));Assert.Empty(p.Materials);
    }
    [Fact]
    public void Rational_scaling_is_computed_before_division_without_losing_exact_quantities()
    {
        var s=Source();s=s with{Recipe=s.Recipe with{OutputQuantity=3},Ingredients=[s.Ingredients[0] with{Quantity=3}]};
        var p=ProductionPostingPlanner.Plan(s,false,false);Assert.True(p.Reviewable);Assert.Equal(2,p.Materials.Sum(t=>t.Quantity));
    }
    [Theory][InlineData("unit")][InlineData("duplicate")][InlineData("inactive")][InlineData("self")][InlineData("shelf")][InlineData("planned")]
    public void Invalid_recipe_or_order_is_reported_as_a_blocked_review(string kind)
    {
        var s=Source();
        if(kind=="unit")s=s with{Ingredients=[s.Ingredients[0] with{Unit="box"}]};
        if(kind=="duplicate")s=s with{Ingredients=[s.Ingredients[0],s.Ingredients[0] with{Id=Guid.NewGuid()}]};
        if(kind=="inactive")s=s with{Recipe=s.Recipe with{Active=false}};
        if(kind=="self")s=s with{Recipe=s.Recipe with{OutputItemId=s.Ingredients[0].ItemId}};
        if(kind=="shelf")s=s with{OutputItem=s.OutputItem! with{ShelfLifeDays=null}};
        if(kind=="planned")s=s with{Order=s.Order with{Status="planned",StartedAt=null}};
        var p=ProductionPostingPlanner.Plan(s,true,true);Assert.False(p.Reviewable);Assert.NotEmpty(p.Issues);Assert.Null(p.Output.MaterialCost);
    }
    [Theory][InlineData("reservation")][InlineData("output_reservation")][InlineData("projection")][InlineData("unit")][InlineData("shortage")]
    public void Stock_risks_are_not_ignored_in_favour_of_a_later_batch(string kind)
    {
        var s=Source();
        if(kind=="reservation")s=s with{ReservedItemIds=[s.Ingredients[0].ItemId]};
        if(kind=="output_reservation")s=s with{ReservedItemIds=[s.Recipe.OutputItemId]};
        if(kind=="projection")s=s with{Batches=[s.Batches[0] with{Reconciled=false},s.Batches[1]]};
        if(kind=="unit")s=s with{Batches=[s.Batches[0] with{LedgerUnit="box"},s.Batches[1]]};
        if(kind=="shortage")s=s with{Batches=[s.Batches[0]]};
        var p=ProductionPostingPlanner.Plan(s,true,true);Assert.False(p.Reviewable);Assert.Null(p.Output.MaterialCost);
        if(kind=="projection")Assert.Equal(s.Batches[0].Id,p.Materials[0].BatchId);
    }
    [Fact]
    public void Unknown_cost_is_preserved_and_never_substituted_with_a_catalog_price()
    {
        var s=Source();var p=ProductionPostingPlanner.Plan(s with{Batches=[s.Batches[0] with{UnitCost=null},s.Batches[1]]},true,true);
        Assert.True(p.Reviewable);Assert.Null(p.Output.MaterialCost);Assert.Null(p.Output.UnitCost);Assert.Null(p.Materials[0].UnitCost);Assert.Contains(p.Warnings,w=>w.Code=="unknown_ledger_cost");
    }
    [Fact]
    public void Valuation_rounding_difference_is_explicit_and_requires_a_policy()
    {
        var s=Source();s=s with{Order=s.Order with{PlannedQuantity=3},Recipe=s.Recipe with{OutputQuantity=3},Ingredients=[s.Ingredients[0] with{Quantity=1}],Batches=[s.Batches[0] with{UnitCost=1}]};
        var p=ProductionPostingPlanner.Plan(s,true,true);Assert.False(p.Reviewable);Assert.Equal(1,p.Output.MaterialCost);Assert.Equal(-0.0001m,p.Output.ValuationDifference);
        Assert.Contains(p.Issues,i=>i.Code=="valuation_rounding_requires_policy");
    }
    [Fact]
    public void Fingerprint_is_order_and_culture_independent_and_changes_with_phantoms_or_source_edits()
    {
        var s=Source();var a=ProductionPostingPlanner.Plan(s,false,false);var prior=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("ar-SA");
            var reordered=s with{Order=s.Order with{PlannedQuantity=2.00m},Batches=s.Batches.Reverse().ToArray()};
            Assert.Equal(a.Fingerprint,ProductionPostingPlanner.Plan(reordered,false,false).Fingerprint);
        }
        finally{CultureInfo.CurrentCulture=prior;}
        var phantom=s.Batches[0] with{Id=Guid.NewGuid(),ExpiryDate=Today.AddDays(1),Quantity=1};
        Assert.NotEqual(a.Fingerprint,ProductionPostingPlanner.Plan(s with{Batches=[..s.Batches,phantom]},false,false).Fingerprint);
        Assert.NotEqual(a.Fingerprint,ProductionPostingPlanner.Plan(s with{Recipe=s.Recipe with{Name="Changed recipe"}},false,false).Fingerprint);
        Assert.NotEqual(a.Fingerprint,ProductionPostingPlanner.Plan(s with{ProductionDate=Today.AddDays(1)},false,false).Fingerprint);
    }
    [Fact]
    public void Equal_expiry_and_added_time_have_a_stable_ID_tie_break()
    {
        var s=Source();s=s with{Batches=s.Batches.Select(b=>b with{ExpiryDate=Today.AddDays(1)}).ToArray()};
        var p=ProductionPostingPlanner.Plan(s,false,false);Assert.Single(p.Materials);Assert.Equal(s.Batches[1].Id,p.Materials[0].BatchId);
    }
    [Theory][InlineData("cancelled")][InlineData("reduced")][InlineData("overallocated")][InlineData("foreign")]
    public void Request_allocation_changes_are_part_of_review_and_cannot_silently_overproduce(string kind)
    {
        var s=Source();var request=new ProductionRequestReview(Guid.NewGuid(),Guid.NewGuid(),2,Guid.NewGuid(),"accepted",true,3,3,2);
        var p=ProductionPostingPlanner.Plan(s with{Requests=[request]},true,true);Assert.True(p.Reviewable);
        var changed=kind=="cancelled"?request with{Status="cancelled"}:kind=="reduced"?request with{AcceptedQuantity=1}:kind=="overallocated"?request with{AllocatedQuantity=4}:request with{InScope=false};
        var next=ProductionPostingPlanner.Plan(s with{Requests=[changed]},true,true);Assert.False(next.Reviewable);Assert.NotEqual(p.Fingerprint,next.Fingerprint);
        Assert.Contains(next.Issues,i=>i.Code=="linked_requests_require_reconciliation");
    }
}
