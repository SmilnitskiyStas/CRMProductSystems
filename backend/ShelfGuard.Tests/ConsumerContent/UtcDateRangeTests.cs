using ShelfGuard.Api;
using Xunit;

namespace ShelfGuard.Tests.ConsumerContent;

public sealed class UtcDateRangeTests
{
    [Fact]
    public void StartOfDay_converts_a_date_only_query_value_to_utc()
    {
        var dateOnlyValue = new DateTime(2026, 9, 7);

        var result = UtcDateRange.StartOfDay(dateOnlyValue);

        Assert.Equal(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void StartOfDay_preserves_an_omitted_date()
    {
        Assert.Null(UtcDateRange.StartOfDay(null));
    }
}
