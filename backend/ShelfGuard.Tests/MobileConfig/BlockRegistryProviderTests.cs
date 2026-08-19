using ShelfGuard.Application.Features.MobileConfig.BlockRegistry;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-538 — <see cref="BlockRegistryProvider"/> (the DI-registered singleton
/// <c>MobileBlocksController</c>-equivalent surface reads through) and the generic
/// <see cref="BlockDefinitionDto"/>/<see cref="BlockPropDefinitionDto"/> mapping the controller uses
/// to serialize whatever the provider returns with no per-block-type branching.
/// </summary>
public sealed class BlockRegistryProviderTests
{
    private readonly BlockRegistryProvider _sut = new();

    [Fact]
    public void GetAll_returns_every_registry_definition()
    {
        var all = _sut.GetAll();

        Assert.Equal(BlockRegistry.Definitions.Count, all.Count);
        Assert.Equal(BlockRegistry.Definitions.Select(d => d.Type).ToHashSet(), all.Select(d => d.Type).ToHashSet());
    }

    [Fact]
    public void TryGet_finds_a_known_type()
    {
        var def = _sut.TryGet("heroBanner");

        Assert.NotNull(def);
        Assert.Equal("heroBanner", def!.Type);
    }

    [Fact]
    public void TryGet_returns_null_for_an_unknown_type()
    {
        Assert.Null(_sut.TryGet("bogusBlock"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryGet_returns_null_for_null_or_empty_input(string? type)
    {
        Assert.Null(_sut.TryGet(type!));
    }

    [Fact]
    public void BlockDefinitionDto_From_maps_every_field_generically()
    {
        var def = BlockRegistry.Definitions.Single(d => d.Type == "promotionCarousel");

        var dto = BlockDefinitionDto.From(def);

        Assert.Equal(def.Type, dto.Type);
        Assert.Equal(def.DisplayName, dto.DisplayName);
        Assert.Equal(def.Icon, dto.Icon);
        Assert.Equal(def.Category, dto.Category);
        Assert.Equal(def.SupportedDataSource, dto.SupportedDataSource);
        Assert.Equal(def.Props.Count, dto.ValidationSchema.Count);
        Assert.Equal(def.DefaultProps.Keys.ToHashSet(), dto.DefaultProps.Keys.ToHashSet());

        var titleProp = def.Props.Single(p => p.Name == "title");
        var titleDto = dto.ValidationSchema.Single(p => p.Name == "title");
        Assert.Equal(titleProp.Type, titleDto.Type);
        Assert.Equal(titleProp.MaxLength, titleDto.MaxLength);
    }

    [Fact]
    public void BlockDefinitionDto_From_maps_every_registered_definition_without_throwing()
    {
        foreach (var def in BlockRegistry.Definitions)
        {
            var dto = BlockDefinitionDto.From(def);
            Assert.Equal(def.Type, dto.Type);
        }
    }
}
