using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Buyer→supplier-employee ratings (TASK-695, Phase 8). Split RLS (ADR-033 style): the buyer
/// writes on its own <c>tenant_isolation</c> (keyed on <c>ClientTenantId</c>), the supplier reads
/// via <c>supplier_read</c>. The buyer-side calls here run on the buyer's own session — no
/// override needed, the buyer's WITH CHECK admits its own row.
/// </summary>
public interface ISupplierEmployeeReviewRepository
{
    /// <summary>
    /// The single order-path rating for one order left by the calling buyer tenant
    /// (<c>Source == "order"</c>). Used for the upsert and for the buyer's "already rated" check.
    /// </summary>
    Task<SupplierEmployeeReview?> GetByOrderAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// The chat-path rating for one (employee, chat session) pair left by the calling buyer tenant
    /// (<c>Source == "chat"</c>). Used for the upsert.
    /// </summary>
    Task<SupplierEmployeeReview?> GetByChatParticipantAsync(
        Guid clientTenantId, Guid chatSessionId, Guid supplierUserId, CancellationToken ct = default);

    /// <summary>
    /// Every chat-path rating the calling buyer tenant left in one chat session — backs the
    /// buyer UI's "which participants have I already rated" view.
    /// </summary>
    Task<IReadOnlyList<SupplierEmployeeReview>> ListByChatSessionForClientAsync(
        Guid clientTenantId, Guid chatSessionId, CancellationToken ct = default);

    Task AddAsync(SupplierEmployeeReview review, CancellationToken ct = default);

    void Update(SupplierEmployeeReview review);

    Task SaveChangesAsync(CancellationToken ct = default);
}
