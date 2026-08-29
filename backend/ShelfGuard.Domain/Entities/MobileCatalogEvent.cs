namespace ShelfGuard.Domain.Entities;

public sealed class MobileCatalogEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid CatalogId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? ConsumerAccountId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public static class MobileCatalogEventType
{
    public const string CatalogView = "catalog_view";
    public const string ProductView = "product_view";
    public const string ProductScan = "product_scan";
    public static bool IsValid(string value) => value is CatalogView or ProductView or ProductScan;
}
