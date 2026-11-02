using ShelfGuard.Application.Features.Erp.Posting;
using Xunit;
namespace ShelfGuard.Tests.Erp;
public sealed class TransferPostingRulesTests
{
    private static TransferDispatchCommand Command() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null," Shipment ",[new(Guid.NewGuid(),2.25m)]);
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1.001)] [InlineData(100000000)]
    public void Invalid_quantities_are_not_rounded(decimal quantity) => Assert.Throws<ArgumentException>(()=>TransferPostingRules.Normalize(Command() with {Items=[new(Guid.NewGuid(),quantity)]}));
    [Fact] public void Duplicate_batch_and_same_location_are_rejected()
    {
        var c=Command(); Assert.Throws<ArgumentException>(()=>TransferPostingRules.Normalize(c with {Items=[c.Items[0],c.Items[0]]}));
        Assert.Throws<ArgumentException>(()=>TransferPostingRules.Normalize(c with {ToLocationId=c.FromLocationId}));
    }
    [Fact] public void Normalized_identity_preserves_explicit_request_and_exact_values()
    { var c=Command(); var normalized=TransferPostingRules.Normalize(c); Assert.Equal(c.RequestId,normalized.RequestId); Assert.Equal("Shipment",normalized.Notes); Assert.Equal("store_to_store",normalized.TransferType); }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" \n ")]
    public void Cancellation_requires_an_explicit_reason(string? reason) => Assert.Throws<ArgumentException>(()=>TransferPostingRules.Reason("cancel",reason));
}
