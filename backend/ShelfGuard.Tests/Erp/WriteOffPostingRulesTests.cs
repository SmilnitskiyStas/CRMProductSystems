using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities;
using Xunit;
namespace ShelfGuard.Tests.Erp;
public sealed class WriteOffPostingRulesTests
{
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(0.001)] [InlineData(100000000)]
    public void Desktop_draft_quantity_is_exact_positive_and_bounded(decimal quantity) =>
        Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.NormalizeDraft(new(Guid.NewGuid(),Guid.NewGuid(),"expired",null,[new(Guid.NewGuid(),quantity)])));
    [Fact] public void Desktop_draft_duplicate_products_and_weak_explanation_are_rejected()
    {
        var line=new WriteOffDraftItem(Guid.NewGuid(),1m);var command=new WriteOffDraftCommand(Guid.NewGuid(),Guid.NewGuid(),"damaged","Reviewed packaging damage",[line]);
        Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.NormalizeDraft(command with{Items=[line,line]}));
        Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.NormalizeDraft(command with{Notes="short"}));
        Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.NormalizeDraft(command with{Notes="Reviewed damage\nwith control"}));
        Assert.Equal("Reviewed packaging damage",WriteOffPostingRules.NormalizeDraft(command with{Notes="  Reviewed packaging damage  "}).Notes);
    }
    private static (WriteOff Source,ProductStock First,ProductStock Second) Example(decimal quantity=4.5m,decimal? loss=90)
    {
        var tenant=Guid.NewGuid();var item=Guid.NewGuid();var loc=Guid.NewGuid();
        var first=new ProductStock{TenantId=tenant,StoreId=loc,ProductId=item,Quantity=3.25m,ExpiryDate=new(2027,1,1)};
        var second=new ProductStock{TenantId=tenant,StoreId=loc,ProductId=item,Quantity=4,ExpiryDate=new(2027,2,1)};
        var w=new WriteOff{TenantId=tenant,StoreId=loc,Reason="expired",Status="pending_approval",TotalLossAmount=loss??0};w.Items.Add(new(){WriteOffId=w.Id,ProductId=item,Quantity=quantity,LossAmount=loss});
        return(w,first,second);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1.001)] [InlineData(100000000)]
    public void Quantity_precision_and_bounds_are_never_rounded(decimal qty)
    {var(w,a,b)=Example(qty);Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.Allocate(w,[a,b]));}
    [Fact] public void Shortage_never_returns_a_partial_plan()
    {var(w,a,b)=Example(8);Assert.Throws<InventoryPostingConflictException>(()=>WriteOffPostingRules.Allocate(w,[a,b]));}
    [Fact] public void FEFO_is_independent_of_query_order_and_retains_full_source_money()
    {var(w,a,b)=Example();var plan=WriteOffPostingRules.Allocate(w,[b,a]);Assert.Equal(3.25m,plan[a.Id].Sum(x=>x.Quantity));Assert.Equal(1.25m,plan[b.Id].Sum(x=>x.Quantity));Assert.Equal(90m,plan.Values.SelectMany(x=>x).Sum(x=>x.RetailLoss));}
    [Fact] public void Cumulative_cent_allocation_does_not_create_or_lose_a_cent()
    {
        var(w,a,b)=Example(.03m,.02m);a.Quantity=.01m;b.Quantity=.02m;var p=WriteOffPostingRules.Allocate(w,[a,b]);
        Assert.Equal(.02m,p.Values.SelectMany(x=>x).Sum(x=>x.RetailLoss));Assert.All(p.Values.SelectMany(x=>x),x=>Assert.True(x.RetailLoss>=0));
    }
    [Fact] public void Unknown_source_value_stays_unknown_on_every_cut()
    {var(w,a,b)=Example(loss:null);var p=WriteOffPostingRules.Allocate(w,[a,b]);Assert.All(p.Values.SelectMany(x=>x),x=>Assert.Null(x.RetailLoss));}
    [Fact] public void Explicit_and_unspecified_rows_can_share_one_total_FEFO_take()
    {
        var(w,a,b)=Example(3.25m,65m);w.Items.Add(new(){WriteOffId=w.Id,ProductId=a.ProductId,ProductStockId=b.Id,Quantity=1,LossAmount=20});w.TotalLossAmount=85;
        var p=WriteOffPostingRules.Allocate(w,[a,b]);Assert.Equal(1,p[b.Id].Single().Quantity);Assert.Equal(3.25m,p[a.Id].Single().Quantity);
    }
    [Theory] [InlineData(null)] [InlineData("too short")]
    public void Reversal_and_rejection_require_an_explicit_explanation(string? reason)
    {Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.Reason("reverse",reason));Assert.Throws<ArgumentException>(()=>WriteOffPostingRules.Reason("reject",reason));}
    [Fact] public void An_inconsistent_stored_summary_requires_correction_before_posting()
    {var(w,a,b)=Example();w.TotalLossAmount=89.99m;Assert.Throws<InventoryPostingConflictException>(()=>WriteOffPostingRules.Allocate(w,[a,b]));}
}
