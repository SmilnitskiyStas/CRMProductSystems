using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using Xunit;
namespace ShelfGuard.Tests.Stock;

public sealed class BatchEligibilityTests
{
    [Theory][InlineData("temp_violation")][InlineData("expired")][InlineData("archived")][InlineData("sold_out")]
    public void Flagged_batches_cannot_be_used_even_if_expiry_is_in_the_future(string status)=>
        Assert.False(StockStatus.IsUsable(status,5,new(2027,1,1),new(2026,10,27)));
    [Theory][InlineData("safe")][InlineData("warning")][InlineData("critical")][InlineData("needs_verification")]
    public void Availability_requires_positive_quantity_and_a_future_expiry(string status)
    {
        var today=new DateOnly(2026,10,27);Assert.True(StockStatus.IsUsable(status,1,today.AddDays(1),today));
        Assert.False(StockStatus.IsUsable(status,0,today.AddDays(1),today));Assert.False(StockStatus.IsUsable(status,1,today,today));
    }
    [Theory][InlineData(0)][InlineData(2)]
    public void Recalculation_preserves_temperature_hold_during_depletion_restoration_and_reads(int qty)
    {
        var batch=new ProductStock{Status="temp_violation",Quantity=qty,ExpiryDate=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))};
        Assert.Equal("temp_violation",StockStatus.Recalculate(batch));
    }
}
