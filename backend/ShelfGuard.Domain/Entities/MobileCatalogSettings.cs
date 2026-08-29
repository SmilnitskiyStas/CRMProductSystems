namespace ShelfGuard.Domain.Entities;

public sealed class MobileCatalogSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = "Каталог";
    public string Description { get; set; } = string.Empty;
    public string? BannerUrl { get; set; }
    public string LayoutMode { get; set; } = "grid";
    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = "draft";
    public DateTime PublishAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnpublishAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public ICollection<MobileCatalogItem> Items { get; set; } = new List<MobileCatalogItem>();
    public ICollection<MobileCatalogLocation> Locations { get; set; } = new List<MobileCatalogLocation>();
}

public static class MobileCatalogPublicationStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Published = "published";
    public const string Archived = "archived";
}
