using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;
using Xunit;

namespace ShelfGuard.Tests.ConsumerContent;

/// <summary>
/// TASK-572 — <c>ConsumerContentController.GetCatalogByIds</c> HTTP layer: the empty-ids
/// short-circuit (never calls the service, so no [RequireConsumerFeature]-mocking is needed for that
/// case) and the id-count clamp before the service call. [RequireConsumerFeature] itself is proven
/// on this action in <c>ConsumerContentControllerFeatureGateTests</c>.
/// </summary>
public sealed class ConsumerContentControllerGetCatalogByIdsTests
{
    private readonly IConsumerContentService _service = Substitute.For<IConsumerContentService>();
    private readonly ConsumerContentController _controller;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    public ConsumerContentControllerGetCatalogByIdsTests()
    {
        _controller = new ConsumerContentController(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task GetCatalogByIds_EmptyIds_ReturnsOkEmptyArrayWithoutCallingService()
    {
        var result = await _controller.GetCatalogByIds(_tenantId, _storeId, [], CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ConsumerCatalogItemDto>>(ok.Value));
        await _service.DidNotReceive().GetCatalogByIdsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCatalogByIds_MoreThan30Ids_ClampsToFirst30BeforeCallingService()
    {
        var ids = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        _service.GetCatalogByIdsAsync(_tenantId, _storeId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ConsumerCatalogItemDto>?)Array.Empty<ConsumerCatalogItemDto>(), (string?)null));

        await _controller.GetCatalogByIds(_tenantId, _storeId, ids, CancellationToken.None);

        await _service.Received(1).GetCatalogByIdsAsync(
            _tenantId, _storeId,
            Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 30 && l.SequenceEqual(ids.Take(30))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCatalogByIds_TenantNotFound_ReturnsNotFound()
    {
        _service.GetCatalogByIdsAsync(_tenantId, _storeId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ConsumerCatalogItemDto>?)null, "Tenant not found."));

        var result = await _controller.GetCatalogByIds(_tenantId, _storeId, [Guid.NewGuid()], CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetCatalogByIds_ValidIds_ReturnsOkWithItems()
    {
        var id = Guid.NewGuid();
        var dto = new ConsumerCatalogItemDto(id, "Молоко", null, "шт", 45m, null, null, true);
        _service.GetCatalogByIdsAsync(_tenantId, _storeId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ConsumerCatalogItemDto>?)new List<ConsumerCatalogItemDto> { dto }, (string?)null));

        var result = await _controller.GetCatalogByIds(_tenantId, _storeId, [id], CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<ConsumerCatalogItemDto>>(ok.Value);
        Assert.Single(items);
        Assert.Equal(id, items[0].Id);
    }
}
