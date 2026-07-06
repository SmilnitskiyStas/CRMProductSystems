namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single persistent chat thread between a supplier tenant and a client tenant
/// (B2B marketplace messaging). One thread per (SupplierTenantId, ClientTenantId)
/// pair — no Status/close concept, messages simply keep accumulating like a
/// personal correspondence. Either side can initiate: the supplier from the
/// "Clients" tab, or the client from the supplier's public marketplace page.
/// </summary>
public sealed class SupplierChatSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierTenantId { get; set; }
    public Guid ClientTenantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SupplierChatMessage> Messages { get; init; } = new List<SupplierChatMessage>();
}
