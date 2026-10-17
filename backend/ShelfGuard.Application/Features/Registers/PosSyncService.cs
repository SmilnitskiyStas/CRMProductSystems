using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Registers;

public sealed class PosSyncService : IPosSyncService
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 2000;

    private readonly ICatalogSyncRepository _catalog;

    public PosSyncService(ICatalogSyncRepository catalog) => _catalog = catalog;

    public async Task<SyncCatalogResponse> GetCatalogAsync(
        DateTime? since, Guid? afterId, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var serverTime = DateTime.UtcNow;

        var rows = await _catalog.GetChangedSinceAsync(since, afterId, take, ct);
        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();

        var last = page.Count > 0 ? page[^1] : null;

        return new SyncCatalogResponse(
            Items: page.Select(r => new SyncCatalogItemDto(
                r.Id, r.Name, r.Barcodes, r.PriceRetail, r.VatRate, r.Unit, r.IsActive, r.UpdatedAt)).ToList(),
            HasMore: hasMore,
            NextSince: last?.UpdatedAt ?? since,
            NextAfterId: last?.Id ?? afterId,
            ServerTime: serverTime);
    }
}
