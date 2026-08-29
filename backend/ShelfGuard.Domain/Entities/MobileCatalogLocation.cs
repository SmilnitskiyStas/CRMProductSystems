namespace ShelfGuard.Domain.Entities;

public sealed class MobileCatalogLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SettingsId { get; set; }
    public Guid LocationId { get; set; }
    public MobileCatalogSettings? Settings { get; set; }
    public Location? Location { get; set; }
}
