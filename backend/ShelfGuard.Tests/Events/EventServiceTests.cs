using NSubstitute;
using ShelfGuard.Application.Features.Events;
using ShelfGuard.Application.Features.Events.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Events;

/// <summary>TASK-588: EventService.RemoveCoefficientAsync — unlink a product/category/segment from a demand event.</summary>
public sealed class EventServiceTests
{
    private readonly IEventRepository _repo = Substitute.For<IEventRepository>();
    private readonly EventService _sut;

    public EventServiceTests()
    {
        _sut = new EventService(_repo);
    }

    [Fact]
    public async Task RemoveCoefficientAsync_NotFound_ReturnsError()
    {
        var eventId = Guid.NewGuid();
        var coefId = Guid.NewGuid();
        _repo.GetCoefficientAsync(coefId, Arg.Any<CancellationToken>()).Returns((DemandEventCoefficient?)null);

        var error = await _sut.RemoveCoefficientAsync(eventId, coefId);

        Assert.Equal("Coefficient not found.", error);
        _repo.DidNotReceive().RemoveCoefficient(Arg.Any<DemandEventCoefficient>());
    }

    [Fact]
    public async Task RemoveCoefficientAsync_BelongsToDifferentEvent_ReturnsError()
    {
        var coef = new DemandEventCoefficient { EventId = Guid.NewGuid(), ScopeType = "product", Coefficient = 2.0m };
        _repo.GetCoefficientAsync(coef.Id, Arg.Any<CancellationToken>()).Returns(coef);

        var error = await _sut.RemoveCoefficientAsync(Guid.NewGuid(), coef.Id);

        Assert.Equal("Coefficient not found.", error);
        _repo.DidNotReceive().RemoveCoefficient(Arg.Any<DemandEventCoefficient>());
    }

    [Fact]
    public async Task RemoveCoefficientAsync_Found_RemovesAndSaves()
    {
        var eventId = Guid.NewGuid();
        var coef = new DemandEventCoefficient { EventId = eventId, ScopeType = "product", Coefficient = 2.0m };
        _repo.GetCoefficientAsync(coef.Id, Arg.Any<CancellationToken>()).Returns(coef);

        var error = await _sut.RemoveCoefficientAsync(eventId, coef.Id);

        Assert.Null(error);
        _repo.Received(1).RemoveCoefficient(coef);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── TASK-593: Scope == "stores" — multi-store demand events ──────────────

    private static UpsertEventRequest Request(
        string scope, Guid? storeId = null, List<Guid>? storeIds = null) => new(
        Name: "Event", EventType: "promo", Scope: scope, StoreId: storeId, StoreIds: storeIds,
        StartsAt: new DateOnly(2026, 1, 1), EndsAt: new DateOnly(2026, 1, 5),
        IsRecurring: false, Notes: null);

    [Fact]
    public async Task CreateAsync_StoresScope_EmptyStoreIds_ReturnsValidationError()
    {
        var (ev, error) = await _sut.CreateAsync(Guid.NewGuid(), null, Request("stores", storeIds: []));

        Assert.Null(ev);
        Assert.Equal("StoreIds is required for stores scope.", error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<DemandEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_StoresScope_NullStoreIds_ReturnsValidationError()
    {
        var (ev, error) = await _sut.CreateAsync(Guid.NewGuid(), null, Request("stores", storeIds: null));

        Assert.Null(ev);
        Assert.Equal("StoreIds is required for stores scope.", error);
    }

    [Fact]
    public async Task CreateAsync_StoresScope_ReplacesStoresWithRequestedSet()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();

        var (ev, error) = await _sut.CreateAsync(
            Guid.NewGuid(), null, Request("stores", storeIds: [storeA, storeB]));

        Assert.Null(error);
        Assert.NotNull(ev);
        Assert.Null(ev!.StoreId); // singular StoreId stays null for "stores" scope
        await _repo.Received(1).ReplaceStoresForEventAsync(
            ev.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 2 && c.Contains(storeA) && c.Contains(storeB)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_StoreScope_ClearsAnyStoresSet()
    {
        var storeId = Guid.NewGuid();

        var (ev, error) = await _sut.CreateAsync(Guid.NewGuid(), null, Request("store", storeId: storeId));

        Assert.Null(error);
        Assert.Equal(storeId, ev!.StoreId);
        await _repo.Received(1).ReplaceStoresForEventAsync(
            ev.Id, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SwitchingFromStoresToNetwork_ClearsStoreLinks()
    {
        var eventId = Guid.NewGuid();
        var existing = new DemandEvent { Id = eventId, Name = "Old", Scope = "stores", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 1, 5) };
        existing.Stores.Add(new DemandEventStore { EventId = eventId, StoreId = Guid.NewGuid() });
        _repo.GetByIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(existing);

        var (ev, error) = await _sut.UpdateAsync(eventId, Request("network"));

        Assert.Null(error);
        Assert.Equal("network", ev!.Scope);
        await _repo.Received(1).ReplaceStoresForEventAsync(
            eventId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoresScope_EmptyStoreIds_ReturnsValidationError_DoesNotSave()
    {
        var eventId = Guid.NewGuid();
        var existing = new DemandEvent { Id = eventId, Name = "Old", Scope = "network", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 1, 5) };
        _repo.GetByIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(existing);

        var (ev, error) = await _sut.UpdateAsync(eventId, Request("stores", storeIds: []));

        Assert.Null(ev);
        Assert.Equal("StoreIds is required for stores scope.", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
