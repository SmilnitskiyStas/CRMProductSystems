namespace ShelfGuard.Tools.PchilkaImport;

public sealed record StockPhaseResult(
    int Created,
    int Near,
    int Mid,
    int Far,
    int Expired,
    Dictionary<Guid, Guid> PrimaryBatchByItemId);

public sealed record TransactionsPhaseResult(
    int Created,
    int SkippedExisting,
    int ItemsCreated,
    int OrdersSkippedNoLines);
