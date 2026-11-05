using System.Globalization;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class ReceiptPostingRulesTests
{
    private static StockReceipt Receipt()
    {
        var tenant=Guid.Parse("11111111-1111-1111-1111-111111111111");
        var r=new StockReceipt { Id=Guid.Parse("22222222-2222-2222-2222-222222222222"), TenantId=tenant,
            DestinationStoreId=Guid.Parse("33333333-3333-3333-3333-333333333333"),Status="ordered" };
        var item=new Item { Id=Guid.Parse("55555555-5555-5555-5555-555555555555"),TenantId=tenant,Name="Flour",Unit="kg",Barcodes=["123"] };
        r.Items.Add(new StockReceiptItem { Id=Guid.Parse("44444444-4444-4444-4444-444444444444"),ReceiptId=r.Id,ProductId=item.Id,Product=item,
            QuantityOrdered=2.25m,PricePurchase=12.50m,ExpiryDate=new(2027,1,1),BatchNumber="A-1" });return r;
    }
    [Fact]
    public void Receiving_fingerprint_has_a_pinned_versioned_contract_and_ignores_process_culture()
    {
        var r=Receipt();var prior=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=new("uk-UA");Assert.Equal("c080d3499f27a4c5273838b4915069291f25e2586edc63efa6b334cef3ea9e01",ReceiptPostingRules.Fingerprint(r)); }
        finally { CultureInfo.CurrentCulture=prior; }
    }
    [Theory]
    [InlineData("2.251")] [InlineData("-1")] [InlineData("100000000")]
    public void Does_not_round_physical_quantities(string raw)
    {
        var r=Receipt();r.Items.First().QuantityReceived=decimal.Parse(raw,CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(()=>ReceiptPostingRules.Validate(r,Guid.NewGuid()));
    }
    [Fact]
    public void Fractional_received_cost_is_preserved_at_ledger_precision_and_null_price_stays_unknown()
    {
        var r=Receipt();ReceiptPostingRules.Validate(r,Guid.NewGuid());Assert.Equal(28.125m,r.Items.First().QuantityOrdered*r.Items.First().PricePurchase);
        r.Items.First().PricePurchase=null;ReceiptPostingRules.Validate(r,Guid.NewGuid());
    }
    [Fact]
    public void Changed_batch_price_unit_or_confirmed_quantity_changes_reviewed_identity()
    {
        var r=Receipt();var fingerprint=ReceiptPostingRules.Fingerprint(r);r.Items.First().QuantityReceived=2.25m;
        Assert.NotEqual(fingerprint,ReceiptPostingRules.Fingerprint(r));fingerprint=ReceiptPostingRules.Fingerprint(r);
        r.Items.First().Product!.Unit="l";Assert.NotEqual(fingerprint,ReceiptPostingRules.Fingerprint(r));
    }
    [Fact]
    public void Ordered_auto_receipt_can_be_received_by_another_operator_after_the_draft_lock_ends()
    {
        var r=Receipt();r.Source=ReceiptSource.AutoReplenishment;r.TakenByUserId=Guid.NewGuid();
        ReceiptPostingRules.Validate(r,Guid.NewGuid());r.Status="draft";
        Assert.Throws<InventoryPostingConflictException>(()=>ReceiptPostingRules.Validate(r,Guid.NewGuid()));
    }
}
