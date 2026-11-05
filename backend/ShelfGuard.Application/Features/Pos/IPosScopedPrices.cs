using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Pos;

public sealed record ScopedPosPrice(PosPriceSnapshot Price, Guid StoreId, Guid RegisterId, PosBasePriceDecision? Basis);
public sealed record ScopedCatalogCursor(Guid TenantId, Guid StoreId, Guid RegisterId, string Generation,
    Guid AfterId, DateTime At, DateTime ExpiresAt);
public interface IPosScopedPrices
{
    IssuedPosPriceSnapshot Issue(CatalogSyncRow row, Guid store, Guid register, DateTime at, DateTime? boundary,
        PosBasePriceDecision? basis);
    ScopedPosPrice? Read(string token, Guid tenant, Guid store, Guid register, string barcode, DateTime soldAt);
    string Cursor(ScopedCatalogCursor cursor);
    ScopedCatalogCursor? ReadCursor(string token, Guid tenant, Guid register, DateTime now);
}
public sealed record ScopedCatalogPage(string Contract, Guid TenantId, Guid StoreId, Guid RegisterId,
    string Generation, string Mode, DateTime At, DateTime? NextBoundary, IReadOnlyList<SyncCatalogItemDto> Items,
    string? Cursor);
public interface IPosScopedCatalog
{
    Task<ScopedCatalogPage> ReadAsync(Guid tenant, Guid register, string? cursor, CancellationToken ct);
}
