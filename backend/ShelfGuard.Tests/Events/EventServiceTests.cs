using NSubstitute;
using ShelfGuard.Application.Features.Events;
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
}
