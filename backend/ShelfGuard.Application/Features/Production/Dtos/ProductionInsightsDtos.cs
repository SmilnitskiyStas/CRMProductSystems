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

// ── GET /api/production/pipeline (TASK-765) ─────────────────────────────────

public record PipelineLineDto(string ItemName, string Unit, decimal Qty);
public record PipelineOrderDto(Guid OrderId, string Status, decimal Qty);
public record PipelineItemQtyDto(Guid ItemId, string ItemName, string Unit, decimal Qty);

public record PipelineNewRequestDto(
    Guid Id, string StoreName, DateOnly NeededBy, DateTime? SubmittedAt, decimal TotalQty, List<PipelineLineDto> Lines);
public record PipelineNewDto(int Count, List<PipelineNewRequestDto> Requests);

public record PipelineInWorkRequestDto(
    Guid Id, string StoreName, DateOnly NeededBy, string ProductionStage, decimal TotalQty,
    List<PipelineLineDto> Lines, List<PipelineOrderDto> Orders);
public record PipelineInWorkDto(int Count, List<PipelineInWorkRequestDto> Requests);
public record PipelineInWorkByStageDto(int AwaitingOrder, int Planned, int InProduction);

public record PipelineReadyRequestDto(
    Guid Id, string StoreName, DateOnly NeededBy, decimal TotalQty, List<PipelineLineDto> Lines, List<Guid> OrderIds);
public record PipelineReadyDto(int Count, List<PipelineReadyRequestDto> Requests, List<PipelineItemQtyDto> ByItem);

public record PipelineShippedRequestDto(
    Guid Id, string StoreName, DateTime? ShippedAt, decimal TotalQty, List<PipelineLineDto> Lines,
    Guid? TransferId, string? TransferStatus);
public record PipelineStoreQtyDto(Guid StoreLocationId, string StoreName, int Requests, decimal Qty);
public record PipelineDayDto(DateOnly Date, int Requests, decimal Qty);
public record PipelineShippedDto(
    int Count, decimal TotalQty, List<PipelineShippedRequestDto> Requests, List<PipelineItemQtyDto> ByItem,
    List<PipelineStoreQtyDto> ByStore, List<PipelineDayDto> PerDay);

public record PipelineStatsDto(int Received, int Accepted, int Shipped, int Rejected);

public record ProductionPipelineDto(
    DateOnly From, DateOnly To,
    PipelineNewDto NewRequests, PipelineInWorkDto InWork, PipelineInWorkByStageDto InWorkByStage,
    PipelineReadyDto Ready, PipelineShippedDto Shipped, PipelineStatsDto Stats);

// ── Analytics (TASK-767) ────────────────────────────────────────────────────
public record AnalyticsTotalsDto(
    int Received, int Accepted, int Rejected, int Shipped,
    decimal RequestedQty, decimal AcceptedQty, decimal ShippedQty,
    decimal? FillRate, double? AvgReviewHours, double? AvgLeadHours);
public record AnalyticsOrdersDoneDto(int Count, decimal Qty, double? AvgHours);
public record AnalyticsItemDto(
    Guid ItemId, string ItemName, string Unit, decimal RequestedQty, decimal AcceptedQty, decimal ShippedQty, decimal? FillRate);
public record AnalyticsStoreDto(
    Guid StoreLocationId, string StoreName, int Requests, decimal RequestedQty, decimal ShippedQty, int Rejected,
    double? AvgLeadHours);
public record AnalyticsDayDto(
    DateOnly Date, int Received, int Accepted, int Rejected, int Shipped, decimal ShippedQty, decimal ProducedQty);
public record AnalyticsReasonDto(string Reason, int Count);
public record AnalyticsPreviousDto(
    DateOnly From, DateOnly To, int Received, int Accepted, int Rejected, int Shipped, decimal ShippedQty);
public record ProductionAnalyticsDto(
    DateOnly From, DateOnly To, AnalyticsTotalsDto Totals, AnalyticsOrdersDoneDto OrdersDone,
    List<AnalyticsItemDto> ByItem, List<AnalyticsStoreDto> ByStore, List<AnalyticsDayDto> PerDay,
    List<AnalyticsReasonDto> RejectionReasons, AnalyticsPreviousDto Previous);
