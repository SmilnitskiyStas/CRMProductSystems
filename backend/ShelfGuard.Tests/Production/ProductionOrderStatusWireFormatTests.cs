using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Production;

public class ProductionOrderStatusWireFormatTests
{
    [Theory]
    [InlineData(ProductionOrderStatus.Planned, "planned")]
    [InlineData(ProductionOrderStatus.InProgress, "in_progress")]
    [InlineData(ProductionOrderStatus.Done, "done")]
    [InlineData(ProductionOrderStatus.Cancelled, "cancelled")]
    public void ToApiString_ReturnsSnakeCase(ProductionOrderStatus status, string expected)
        => Assert.Equal(expected, status.ToApiString());

    [Theory]
    [InlineData("in_progress", ProductionOrderStatus.InProgress)]
    [InlineData("InProgress", ProductionOrderStatus.InProgress)]
    [InlineData("PLANNED", ProductionOrderStatus.Planned)]
    [InlineData("done", ProductionOrderStatus.Done)]
    [InlineData("cancelled", ProductionOrderStatus.Cancelled)]
    public void TryParseApi_AcceptsSnakeCaseAndEnumName(string input, ProductionOrderStatus expected)
    {
        Assert.True(ProductionOrderStatusExtensions.TryParseApi(input, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("42")]
    public void TryParseApi_RejectsInvalid(string? input)
        => Assert.False(ProductionOrderStatusExtensions.TryParseApi(input, out _));
}
