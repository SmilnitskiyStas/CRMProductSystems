namespace ShelfGuard.Application.Features.Production.Dtos;

// ── GET /api/production/overview ────────────────────────────────────────────

public record OverviewNewRequestDto(Guid Id, string StoreName, DateOnly NeededBy, decimal TotalQty);
public record OverviewOrderDto(Guid Id, string RecipeName, decimal PlannedQty, DateTimeOffset? StartedAt);
public record OverviewReadyDto(Guid RequestId, string StoreName, decimal Qty);
public record OverviewExpiringDto(
    string ItemName, decimal Quantity, string Unit, DateOnly ExpiryDate, string LocationName, string Kind);
public record OverviewShortageDto(
    Guid ItemId, string ItemName, string Unit, decimal Required, decimal Available, decimal Missing);

public record ProductionOverviewDto(
    int IncomingRequests,
    int AwaitingOrder,
    int OrdersPlanned,
    int OrdersInProgress,
    int ReadyToShip,
    int Shortages,
    int ExpiringSoon,
    List<OverviewNewRequestDto> NewRequests,
    List<OverviewOrderDto> InProgressOrders,
    List<OverviewReadyDto> ReadyToShipList,
    List<OverviewExpiringDto> Expiring,
    List<OverviewShortageDto> ShortageList);

// ── GET /api/production/plan ────────────────────────────────────────────────

public record PlanBucketDto(string Key, string Label, DateOnly From, DateOnly To);
public record PlanBucketQtyDto(string BucketKey, decimal RequestedQty, decimal PendingQty, decimal ForecastQty);
public record PlanProductDto(
    Guid ItemId, string ItemName, string Unit, bool HasRecipe,
    List<PlanBucketQtyDto> PerBucket,
    decimal TotalRequested, decimal TotalPending, decimal TotalForecast,
    decimal FinishedOnHand, decimal OpenOrderQty, decimal ToProduce);
public record PlanIngredientDto(
    Guid ItemId, string ItemName, string Unit, decimal Required, decimal OnHand, decimal Missing,
    Guid? DefaultSupplierId, string? DefaultSupplierName);
public record ProductionPlanDto(
    DateOnly From, DateOnly To, string Granularity,
    List<PlanBucketDto> Buckets, List<PlanProductDto> Products, List<PlanIngredientDto> Ingredients);

// ── GET /api/production/shipments ───────────────────────────────────────────

public record ShipmentLineDto(Guid ItemId, string ItemName, string Unit, decimal Qty);
public record ReadyShipmentDto(
    Guid RequestId, Guid StoreLocationId, string StoreName, DateOnly NeededBy, string ProductionStage,
    List<ShipmentLineDto> Lines, List<Guid> OrderIds, bool CanShip);
public record ShippedShipmentDto(
    Guid RequestId, Guid StoreLocationId, string StoreName, DateTime? ShippedAt,
    Guid? TransferId, string? TransferStatus, List<ShipmentLineDto> Lines);

// ── GET /api/production/stock ───────────────────────────────────────────────

public record ProductionStockBatchDto(
    Guid Id, string? BatchNumber, decimal Quantity, DateOnly ExpiryDate, int DaysToExpiry,
    string Status, string? SourceType, Guid? SourceId);
public record ProductionStockItemDto(
    Guid ItemId, string ItemName, string Unit, string Kind, decimal TotalQty, DateOnly? NearestExpiry,
    string LocationName, List<ProductionStockBatchDto> Batches);
