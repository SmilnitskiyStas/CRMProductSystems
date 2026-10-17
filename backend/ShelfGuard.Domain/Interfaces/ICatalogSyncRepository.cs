namespace ShelfGuard.Domain.Interfaces;

/// <summary>One catalog row for register apps: just what the till needs offline.</summary>
public sealed record CatalogSyncRow(
    Guid Id,
    string Name,
    IReadOnlyList<string> Barcodes,
    decimal? PriceRetail,
    decimal VatRate,
    string Unit,
    bool IsActive,
    DateTime UpdatedAt);

public interface ICatalogSyncRepository
{
    /// <summary>
    /// Items changed after the (UpdatedAt, Id) cursor, ordered by (UpdatedAt, Id), at most
    /// <paramref name="limit"/> + 1 rows (the extra row lets the caller detect "has more").
    /// Includes inactive items so the till can drop them.
    /// </summary>
    Task<IReadOnlyList<CatalogSyncRow>> GetChangedSinceAsync(
        DateTime? since, Guid? afterId, int limit, CancellationToken ct = default);
}
