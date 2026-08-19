namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// Read-only accessor over <see cref="BlockRegistry.Definitions"/> (TASK-538). An interface (rather
/// than callers referencing the static <see cref="BlockRegistry"/> class directly) so
/// <c>MobileBlocksController</c> can take it as a normal constructor dependency and so a future
/// consumer of this data can substitute a fake/mocked catalog in tests without depending on static
/// state — mirrors the DI-registered-singleton shape suggested for this task, distinct from
/// <c>MobileConfigWhitelists</c>/<c>MobileThemeWhitelists</c> (referenced directly as static classes
/// elsewhere in this feature) because those are pure constants, not something an API layer resolves
/// through DI.
/// </summary>
public interface IBlockRegistryProvider
{
    /// <summary>Every registered block type, in catalog order.</summary>
    IReadOnlyList<BlockDefinition> GetAll();

    /// <summary>The definition for <paramref name="type"/>, or null if it is not a registered block type.</summary>
    BlockDefinition? TryGet(string type);
}
