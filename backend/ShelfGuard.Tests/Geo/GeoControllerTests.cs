using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Geo;
using ShelfGuard.Application.Features.Geo.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Geo;

/// <summary>
/// TASK-648: the thin HTTP layer over <see cref="IGeoService"/> — <c>GET /api/geo/regions</c>
/// returns the service list verbatim as 200 OK. Registry content is covered by
/// <c>UkraineRegionsTests</c>, mapping by <c>GeoServiceTests</c>.
/// </summary>
public sealed class GeoControllerTests
{
    private readonly IGeoService _geoService = Substitute.For<IGeoService>();
    private readonly GeoController _controller;

    public GeoControllerTests()
    {
        _controller = new GeoController(_geoService);
    }

    [Fact]
    public void GetRegions_ReturnsOk_WithServiceList()
    {
        var regions = new List<RegionDto>
        {
            new("UA-32", "Київська", "oblast", null),
            new("UA-18-ZHYTOMYR", "Житомир", "city", "UA-18"),
        };
        _geoService.GetRegions().Returns(regions);

        var result = _controller.GetRegions();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(regions, ok.Value);
    }
}
