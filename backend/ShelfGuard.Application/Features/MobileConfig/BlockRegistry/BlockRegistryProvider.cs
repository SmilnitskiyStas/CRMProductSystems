namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// See <see cref="IBlockRegistryProvider"/>. Stateless wrapper over the compile-time
/// <see cref="BlockRegistry.Definitions"/> list — registered as a DI singleton (TASK-538) since the
/// catalog never changes at runtime and building the lookup dictionary once is cheap and safe to
/// share across requests.
/// </summary>
public sealed class BlockRegistryProvider : IBlockRegistryProvider
{
    private readonly IReadOnlyDictionary<string, BlockDefinition> _byType =
        BlockRegistry.Definitions.ToDictionary(d => d.Type);

    public IReadOnlyList<BlockDefinition> GetAll() => BlockRegistry.Definitions;

    public BlockDefinition? TryGet(string type) =>
        !string.IsNullOrEmpty(type) && _byType.TryGetValue(type, out var def) ? def : null;
}
