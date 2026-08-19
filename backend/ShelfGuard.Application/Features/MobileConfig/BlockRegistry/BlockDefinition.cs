namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// One block type's full catalog entry (TASK-538, CLAUDE CODE SPEC §12) — <see cref="DisplayName"/>/
/// <see cref="Icon"/>/<see cref="Category"/> drive the App Builder's block palette (TASK-539);
/// <see cref="Props"/> is this block type's <c>validationSchema</c> — TASK-540's Property Editor
/// generates its form from it, field by field, with no per-block-type branching required.
/// <see cref="DefaultProps"/> seeds a freshly-dropped block instance and is deliberately *derived*
/// from each <see cref="BlockPropDefinition.Default"/> (not a second, independently-authored
/// dictionary) so the two can never silently drift apart. <see cref="SupportedDataSource"/> is a
/// human-readable description of which backend read this block type is meant to bind to — some
/// point at data that already exists and is documented with its real endpoint; others (see
/// <c>BlockRegistry.Definitions</c>'s <c>newsList</c>/<c>storeList</c> remarks) honestly flag that
/// no such backend read exists yet rather than inventing one.
/// </summary>
public sealed record BlockDefinition(
    string Type,
    string DisplayName,
    string Icon,
    string Category,
    IReadOnlyList<BlockPropDefinition> Props,
    string SupportedDataSource)
{
    public IReadOnlyDictionary<string, object?> DefaultProps =>
        Props.ToDictionary(p => p.Name, p => p.Default);
}
