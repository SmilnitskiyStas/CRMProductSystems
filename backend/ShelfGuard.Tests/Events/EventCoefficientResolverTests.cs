using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Events;

public sealed class EventCoefficientResolverTests
{
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid SegmentMilk = Guid.NewGuid();
    private static readonly Guid CategoryDairy = Guid.NewGuid();

    private static DemandEvent Event(params DemandEventCoefficient[] coefs)
    {
        var ev = new DemandEvent { Name = "test", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 12, 31) };
        foreach (var c in coefs) ev.Coefficients.Add(c);
        return ev;
    }

    private static DemandEventCoefficient Coef(string scopeType, Guid? scopeId, decimal k) =>
        new() { ScopeType = scopeType, ScopeId = scopeId, Coefficient = k };

    [Fact]
    public void No_events_means_neutral_multiplier()
    {
        Assert.Equal(1m, EventCoefficientResolver.Resolve([], ProductA, SegmentMilk, CategoryDairy));
    }

    [Fact]
    public void Most_specific_scope_wins_within_one_event()
    {
        var ev = Event(
            Coef("category", CategoryDairy, 1.2m),
            Coef("segment", SegmentMilk, 1.5m),
            Coef("product", ProductA, 2.0m));

        var k = EventCoefficientResolver.Resolve([ev], ProductA, SegmentMilk, CategoryDairy);

        Assert.Equal(2.0m, k); // product beats segment beats category
    }

    [Fact]
    public void Falls_back_to_segment_then_category()
    {
        var ev = Event(
            Coef("category", CategoryDairy, 1.2m),
            Coef("segment", SegmentMilk, 1.5m));

        Assert.Equal(1.5m, EventCoefficientResolver.Resolve([ev], ProductA, SegmentMilk, CategoryDairy));
        Assert.Equal(1.2m, EventCoefficientResolver.Resolve([ev], ProductA, null, CategoryDairy));
    }

    [Fact]
    public void Non_matching_event_is_neutral()
    {
        var ev = Event(Coef("segment", Guid.NewGuid(), 3.0m));

        Assert.Equal(1m, EventCoefficientResolver.Resolve([ev], ProductA, SegmentMilk, CategoryDairy));
    }

    [Fact]
    public void Multiple_events_multiply()
    {
        var holiday = Event(Coef("category", CategoryDairy, 1.2m));
        var localFair = Event(Coef("segment", SegmentMilk, 1.5m));

        var k = EventCoefficientResolver.Resolve([holiday, localFair], ProductA, SegmentMilk, CategoryDairy);

        Assert.Equal(1.8m, k); // 1.2 × 1.5
    }

    [Fact]
    public void Recurring_event_wraps_over_new_year()
    {
        var newYear = new DemandEvent
        {
            Name = "Новий рік",
            IsRecurring = true,
            StartsAt = new(2026, 12, 25),
            EndsAt = new(2026, 1, 2),
        };

        Assert.True(newYear.IsActiveOn(new(2027, 12, 28)));
        Assert.True(newYear.IsActiveOn(new(2028, 1, 1)));
        Assert.False(newYear.IsActiveOn(new(2027, 6, 15)));
    }
}
