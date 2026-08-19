using ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// Wire shape of one <see cref="BlockPropDefinition"/> — a single entry in a block's
/// <c>validationSchema</c> (TASK-538, CLAUDE CODE SPEC §12). <see cref="From"/> is a generic,
/// field-by-field mapping with no per-block-type branching, so <c>MobileBlocksController</c> never
/// needs an if/else per block type.
/// </summary>
public sealed record BlockPropDefinitionDto(
    string Name,
    string Type,
    bool Required,
    object? Default,
    int? MinLength,
    int? MaxLength,
    int? Min,
    int? Max,
    IReadOnlyList<string>? AllowedValues,
    int? MinItems,
    int? MaxItems)
{
    public static BlockPropDefinitionDto From(BlockPropDefinition prop) => new(
        prop.Name,
        prop.Type,
        prop.Required,
        prop.Default,
        prop.MinLength,
        prop.MaxLength,
        prop.Min,
        prop.Max,
        prop.AllowedValues,
        prop.MinItems,
        prop.MaxItems);
}

/// <summary>
/// Wire shape of one <see cref="BlockDefinition"/> — the response of
/// <c>GET /api/v1/mobile/blocks</c>/<c>GET /api/v1/mobile/blocks/{type}</c> (TASK-538).
/// </summary>
public sealed record BlockDefinitionDto(
    string Type,
    string DisplayName,
    string Icon,
    string Category,
    IReadOnlyDictionary<string, object?> DefaultProps,
    IReadOnlyList<BlockPropDefinitionDto> ValidationSchema,
    string SupportedDataSource)
{
    public static BlockDefinitionDto From(BlockDefinition def) => new(
        def.Type,
        def.DisplayName,
        def.Icon,
        def.Category,
        def.DefaultProps,
        def.Props.Select(BlockPropDefinitionDto.From).ToList(),
        def.SupportedDataSource);
}
