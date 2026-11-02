namespace ShelfGuard.Application.Features.Erp.Backfill;

public sealed record InventorySnapshot(Guid TenantId, Guid LocationId, DateTime CapturedAt, string DatabaseSnapshot,
    IReadOnlyList<SnapshotItem> Items, IReadOnlyList<SnapshotZone> Zones, IReadOnlyList<SnapshotBatch> Batches,
    IReadOnlyList<SnapshotMovement> Movements, IReadOnlyList<SnapshotEvent> Events, IReadOnlyList<SnapshotSale> Sales,
    IReadOnlyList<SnapshotProduction> Production, IReadOnlyList<SnapshotLedger> Ledger, IReadOnlyList<SnapshotProjection> Projections);
public sealed record SnapshotItem(Guid Id, Guid TenantId, string Name, string Unit = "шт");
public sealed record SnapshotZone(Guid Id, Guid LocationId);
public sealed record SnapshotBatch(Guid Id, Guid TenantId, Guid ItemId, Guid LocationId, Guid? ZoneId,
    decimal Quantity, decimal QuantityInitial, DateTime AddedAt, string? SourceType, Guid? SourceId,
    string? BatchNumber, DateOnly ExpiryDate);
public sealed record SnapshotMovement(Guid Id, Guid TenantId, Guid ItemId, Guid? BatchId, string Type,
    Guid? FromLocationId, Guid? ToLocationId, decimal Quantity, decimal? Before, decimal? After,
    string? ReferenceType, Guid? ReferenceId, DateTime CreatedAt);
public sealed record SnapshotEvent(Guid Id, Guid TenantId, Guid? ItemId, Guid? BatchId, Guid? LocationId,
    string Type, decimal? Delta, string? Metadata, DateTime CreatedAt);
public sealed record SnapshotSale(Guid Id, Guid TenantId, Guid TransactionId, Guid ItemId, Guid LocationId,
    Guid? PrimaryBatchId, decimal Quantity);
public sealed record SnapshotProduction(Guid Id, Guid TenantId, Guid OrderId, Guid ItemId, Guid LocationId,
    Guid BatchId, decimal Quantity);
public sealed record SnapshotLedger(Guid Id, Guid TenantId, Guid ItemId, Guid LocationId, Guid BatchId, decimal Delta);
public sealed record SnapshotProjection(Guid Id, Guid TenantId, Guid ItemId, Guid LocationId, Guid BatchId,
    decimal Quantity, decimal ReservedQuantity);

public interface IInventorySnapshotReader
{
    Task<InventorySnapshot> ReadAsync(Guid tenantId, Guid locationId, int maxRows = 20_000, CancellationToken ct = default, Guid? operatorId = null);
}

public sealed class SnapshotLimitExceededException(string stream, int limit)
    : InvalidOperationException($"Snapshot stream '{stream}' exceeds its {limit} row limit. No partial report was produced.");
