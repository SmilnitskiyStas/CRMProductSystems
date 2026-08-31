using ShelfGuard.Application.Features.Geo;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.Geo;

/// <summary>
/// TASK-648: <see cref="GeoService"/> maps the static <see cref="UkraineRegions"/> registry
/// to <c>RegionDto</c> 1:1, no DB.
/// </summary>
public sealed class GeoServiceTests
{
    private readonly GeoService _service = new();

    [Fact]
    public void GetRegions_ReturnsEveryRegistryEntry_InOrder()
    {
        var dtos = _service.GetRegions();

        Assert.Equal(UkraineRegions.All.Count, dtos.Count);
        Assert.Equal(
            UkraineRegions.All.Select(r => (r.Code, r.NameUa, r.Kind, r.ParentCode)),
            dtos.Select(d => (d.Code, d.NameUa, d.Kind, d.ParentCode)));
    }

    [Fact]
    public void GetRegions_IncludesBothKyivCodes_Distinct()
    {
        var dtos = _service.GetRegions();

        Assert.Contains(dtos, d => d.Code == "UA-30" && d.Kind == "oblast");
        Assert.Contains(dtos, d => d.Code == "UA-32" && d.Kind == "oblast");
    }
}
