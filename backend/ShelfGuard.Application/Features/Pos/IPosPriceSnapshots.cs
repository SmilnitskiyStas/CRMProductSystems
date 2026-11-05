using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Pos;

public sealed record PosPriceSnapshot(Guid TenantId, Guid ItemId, string Name, string[] Barcodes,
    Guid? CategoryId, string Unit, decimal Price, decimal VatRate, decimal? UnitCost,
    DateTime IssuedAt, DateTime ExpiresAt);

public sealed record IssuedPosPriceSnapshot(string Token, DateTime ExpiresAt);

/// <summary>Server-authenticated catalog values. Cost stays confidential inside the encrypted token.</summary>
public interface IPosPriceSnapshots
{
    IssuedPosPriceSnapshot Issue(CatalogSyncRow item, DateTime issuedAt);
    PosPriceSnapshot? Read(string token, Guid tenantId, string barcode, DateTime soldAt);
}
