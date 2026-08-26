using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

/// <summary>
/// TASK-572 — HTTP layer wrapping <see cref="IItemService.GetPagedAsync"/>'s new
/// <c>search</c>/<c>ids</c> query params. The load-bearing case is
/// <see cref="GetAll_WithIds_ClampsTo30AndForcesPageOnePageSize30"/>: an <c>ids</c> caller wants
/// exactly those items, not a paginated browse (ADR-032 decision 3), so the controller must
/// override whatever <c>page</c>/<c>pageSize</c> the caller sent.
/// </summary>
public sealed class ItemsControllerTests
{
    private readonly IItemService _catalog = Substitute.For<IItemService>();
    private readonly ItemsController _controller;

    public ItemsControllerTests()
    {
        _controller = new ItemsController(_catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task GetAll_NoSearchOrIds_BehavesByteIdenticallyToToday()
    {
        _catalog.GetPagedAsync(Guid.Empty, null, null, null, null, null, null, null, 1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemDto>());

        var result = await _controller.GetAll(null, null, null, page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
        await _catalog.Received(1).GetPagedAsync(
            Guid.Empty, null, null, null, null, null, null, null, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAll_WithSearch_PassesSearchThroughUnchanged()
    {
        _catalog.GetPagedAsync(Guid.Empty, null, null, null, "молок", null, null, null, 1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemDto>());

        var result = await _controller.GetAll(null, null, null, search: "молок", page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
        await _catalog.Received(1).GetPagedAsync(
            Guid.Empty, null, null, null, "молок", null, null, null, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAll_WithIds_ClampsTo30AndForcesPageOnePageSize30()
    {
        var ids = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        _catalog.GetPagedAsync(
                Guid.Empty, null, null, null, null,
                Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 30 && l.SequenceEqual(ids.Take(30))),
                null, null, 1, 30, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemDto>());

        // Caller asks for page 3 / pageSize 10 — the ids branch must override both.
        var result = await _controller.GetAll(null, null, null, ids: ids, page: 3, pageSize: 10);

        Assert.IsType<OkObjectResult>(result);
        await _catalog.Received(1).GetPagedAsync(
            Guid.Empty, null, null, null, null,
            Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 30 && l.SequenceEqual(ids.Take(30))),
            null, null, 1, 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAll_WithFewIds_ForcesPageOnePageSize30WithoutTruncating()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _catalog.GetPagedAsync(
                Guid.Empty, null, null, null, null,
                Arg.Is<IReadOnlyList<Guid>>(l => l.SequenceEqual(ids)),
                null, null, 1, 30, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemDto>());

        var result = await _controller.GetAll(null, null, null, ids: ids, page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
        await _catalog.Received(1).GetPagedAsync(
            Guid.Empty, null, null, null, null,
            Arg.Is<IReadOnlyList<Guid>>(l => l.SequenceEqual(ids)),
            null, null, 1, 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAll_WithSortByAndSortDescending_PassesBothThrough()
    {
        _catalog.GetPagedAsync(Guid.Empty, null, null, null, null, null, "retailprice", true, 1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemDto>());

        var result = await _controller.GetAll(null, null, null, sortBy: "retailprice", sortDescending: true, page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
        await _catalog.Received(1).GetPagedAsync(
            Guid.Empty, null, null, null, null, null, "retailprice", true, 1, 50, Arg.Any<CancellationToken>());
    }
}
