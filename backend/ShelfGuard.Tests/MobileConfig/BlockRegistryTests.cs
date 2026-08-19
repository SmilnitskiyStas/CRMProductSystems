using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.BlockRegistry;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-538 — the static Block Registry catalog. The most load-bearing assertion is the last one:
/// the registry's type set must exactly match <see cref="MobileConfigWhitelists.BlockTypes"/>
/// (TASK-532's already-enforced <c>pages.*.blocks[].type</c> allowlist), the same
/// "agreement test" pattern <c>MobileConfigSchemaContractTests</c> already uses to guard TASK-533's
/// contract against silently drifting from TASK-532's whitelist.
/// </summary>
public sealed class BlockRegistryTests
{
    [Fact]
    public void Registry_defines_all_12_Core_Blocks_V1_types()
    {
        Assert.Equal(12, BlockRegistry.Definitions.Count);
    }

    [Fact]
    public void Registry_type_set_matches_MobileConfigWhitelists_BlockTypes_exactly()
    {
        var registryTypes = BlockRegistry.Definitions.Select(d => d.Type).ToHashSet();

        Assert.Equal(MobileConfigWhitelists.BlockTypes.ToHashSet(), registryTypes);
    }

    [Fact]
    public void Registry_has_no_duplicate_types()
    {
        var types = BlockRegistry.Definitions.Select(d => d.Type).ToList();

        Assert.Equal(types.Count, types.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Every_definition_has_non_empty_display_metadata(BlockDefinition def)
    {
        Assert.False(string.IsNullOrWhiteSpace(def.Type));
        Assert.False(string.IsNullOrWhiteSpace(def.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(def.Icon));
        Assert.False(string.IsNullOrWhiteSpace(def.Category));
        Assert.False(string.IsNullOrWhiteSpace(def.SupportedDataSource));
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Every_prop_has_a_known_type_and_non_empty_name(BlockDefinition def)
    {
        var knownTypes = new[]
        {
            BlockPropTypes.String, BlockPropTypes.Int, BlockPropTypes.Bool,
            BlockPropTypes.Enum, BlockPropTypes.Url, BlockPropTypes.StringArray,
        };

        foreach (var prop in def.Props)
        {
            Assert.False(string.IsNullOrWhiteSpace(prop.Name));
            Assert.Contains(prop.Type, knownTypes);
        }
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Every_enum_or_stringArray_prop_declares_allowed_values(BlockDefinition def)
    {
        foreach (var prop in def.Props.Where(p => p.Type is BlockPropTypes.Enum or BlockPropTypes.StringArray))
        {
            Assert.NotNull(prop.AllowedValues);
            Assert.NotEmpty(prop.AllowedValues!);
        }
    }

    [Fact]
    public void DefaultProps_is_derived_from_Props_defaults_for_every_definition()
    {
        foreach (var def in BlockRegistry.Definitions)
        {
            Assert.Equal(def.Props.Count, def.DefaultProps.Count);
            foreach (var prop in def.Props)
            {
                Assert.True(def.DefaultProps.ContainsKey(prop.Name));
                Assert.Equal(prop.Default, def.DefaultProps[prop.Name]);
            }
        }
    }

    [Theory]
    [InlineData("heroBanner", "heightPx", 190, 120, 260)]
    [InlineData("bannerCarousel", "cardWidthPx", 280, 200, 360)]
    [InlineData("promotionCarousel", "cardWidthPx", 210, 150, 270)]
    [InlineData("productCarousel", "cardWidthPx", 170, 120, 220)]
    public void Resizable_block_types_declare_their_new_size_prop_with_expected_bounds(
        string blockType, string propName, int expectedDefault, int expectedMin, int expectedMax)
    {
        var def = BlockRegistry.Definitions.Single(d => d.Type == blockType);
        var prop = def.Props.Single(p => p.Name == propName);
        Assert.Equal(BlockPropTypes.Int, prop.Type);
        Assert.Equal(expectedDefault, prop.Default);
        Assert.Equal(expectedMin, prop.Min);
        Assert.Equal(expectedMax, prop.Max);
        Assert.False(prop.Required);
    }

    [Fact]
    public void QuickActions_allowed_values_reuse_the_navigation_types_whitelist()
    {
        var quickActions = BlockRegistry.Definitions.Single(d => d.Type == "quickActions");
        var actionsProp = quickActions.Props.Single(p => p.Name == "actions");

        Assert.Equal(MobileConfigWhitelists.NavigationTypes.ToHashSet(), actionsProp.AllowedValues!.ToHashSet());
    }

    public static IEnumerable<object[]> AllDefinitions() =>
        BlockRegistry.Definitions.Select(d => new object[] { d });
}
