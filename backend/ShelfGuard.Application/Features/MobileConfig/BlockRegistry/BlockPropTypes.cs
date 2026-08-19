namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// Primitive kinds a block's <c>props</c> field can declare via <see cref="BlockPropDefinition.Type"/>
/// (TASK-538, Block Registry). Plain string constants — the same "kind travels as a string, not a
/// C# enum" convention <c>MobileConfigurationVersionStatus</c> already uses for
/// <c>MobileConfigurationVersion.Status</c> — so these serialize as-is in the
/// <c>GET /api/v1/mobile/blocks</c> response with no extra <c>JsonStringEnumConverter</c>
/// configuration (System.Text.Json serializes a real C# enum as a number by default, which would be
/// useless to TASK-540's Property Editor without additional setup this repo doesn't otherwise need).
/// </summary>
public static class BlockPropTypes
{
    /// <summary>A short free-text string. See <see cref="BlockPropDefinition.MinLength"/>/<see cref="BlockPropDefinition.MaxLength"/>.</summary>
    public const string String = "string";

    /// <summary>An integer. See <see cref="BlockPropDefinition.Min"/>/<see cref="BlockPropDefinition.Max"/>.</summary>
    public const string Int = "int";

    /// <summary>A boolean.</summary>
    public const string Bool = "bool";

    /// <summary>A string restricted to <see cref="BlockPropDefinition.AllowedValues"/>.</summary>
    public const string Enum = "enum";

    /// <summary>An absolute http(s) URL. See <see cref="BlockPropDefinition.MaxLength"/>.</summary>
    public const string Url = "url";

    /// <summary>
    /// An array of strings. When <see cref="BlockPropDefinition.AllowedValues"/> is set, every item
    /// must belong to it (used by <c>quickActions.actions</c>). See
    /// <see cref="BlockPropDefinition.MinItems"/>/<see cref="BlockPropDefinition.MaxItems"/>.
    /// </summary>
    public const string StringArray = "stringArray";
}
