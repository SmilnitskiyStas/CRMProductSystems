using ShelfGuard.Application.Features.Stock;
using Xunit;

namespace ShelfGuard.Tests.Stock;

public sealed class StockStatusTests
{
    private static DateTime Now => DateTime.UtcNow;

    [Fact]
    public void Compute_ZeroQuantity_ReturnsSoldOut()
    {
        var status = StockStatus.Compute(0, DateOnly.FromDateTime(Now.AddDays(30)), Now);
        Assert.Equal("sold_out", status);
    }

    [Fact]
    public void Compute_ExpiryToday_ReturnsExpired()
    {
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now), Now);
        Assert.Equal("expired", status);
    }

    [Fact]
    public void Compute_ExpiredYesterday_ReturnsExpired()
    {
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now.AddDays(-1)), Now);
        Assert.Equal("expired", status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Compute_1to6DaysLeft_ReturnsCritical(int days)
    {
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now.AddDays(days)), Now);
        Assert.Equal("critical", status);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(14)]
    public void Compute_7to14DaysLeft_ReturnsWarning(int days)
    {
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now.AddDays(days)), Now);
        Assert.Equal("warning", status);
    }

    [Fact]
    public void Compute_Over14DaysAndFreshCheck_ReturnsSafe()
    {
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now.AddDays(30)), Now);
        Assert.Equal("safe", status);
    }

    [Fact]
    public void Compute_Over14DaysButStaleCheck_ReturnsNeedsVerification()
    {
        var staleCheck = Now.AddDays(-91);
        var status = StockStatus.Compute(10, DateOnly.FromDateTime(Now.AddDays(30)), staleCheck);
        Assert.Equal("needs_verification", status);
    }

    [Fact]
    public void Compute_SoldOutTakesPriorityOverExpiry()
    {
        // quantity=0 beats expiry check
        var status = StockStatus.Compute(0, DateOnly.FromDateTime(Now.AddDays(-5)), Now);
        Assert.Equal("sold_out", status);
    }

    [Fact]
    public void DaysLeft_FutureDate_ReturnsPositive()
    {
        var days = StockStatus.DaysLeft(DateOnly.FromDateTime(Now.AddDays(10)));
        Assert.True(days >= 9 && days <= 11);
    }

    [Fact]
    public void DaysLeft_PastDate_ReturnsNegativeOrZero()
    {
        var days = StockStatus.DaysLeft(DateOnly.FromDateTime(Now.AddDays(-5)));
        Assert.True(days <= -4);
    }
}
