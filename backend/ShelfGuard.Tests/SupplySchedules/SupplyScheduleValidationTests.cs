using ShelfGuard.Application.Features.SupplySchedules;
using Xunit;

namespace ShelfGuard.Tests.SupplySchedules;

public sealed class SupplyScheduleValidationTests
{
    [Fact]
    public void Empty_days_are_rejected()
    {
        Assert.NotNull(SupplyScheduleService.ValidateDays([]));
        Assert.NotNull(SupplyScheduleService.ValidateDays(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    public void Out_of_range_days_are_rejected(int day)
    {
        Assert.NotNull(SupplyScheduleService.ValidateDays([1, day]));
    }

    [Fact]
    public void Valid_iso_days_pass()
    {
        Assert.Null(SupplyScheduleService.ValidateDays([1, 3, 5]));
        Assert.Null(SupplyScheduleService.ValidateDays([7]));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(61, false)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(null, true)]
    public void Lead_days_range_is_enforced(int? leadDays, bool valid)
    {
        var error = SupplyScheduleService.ValidateLeadDays(leadDays);
        if (valid) Assert.Null(error);
        else Assert.NotNull(error);
    }

    [Fact]
    public void Normalize_dedupes_and_sorts()
    {
        Assert.Equal([1, 3, 5], SupplyScheduleService.Normalize([5, 1, 3, 1, 5]));
    }
}
