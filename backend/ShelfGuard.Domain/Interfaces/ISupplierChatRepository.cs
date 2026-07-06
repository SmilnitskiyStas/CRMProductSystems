using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for the supplier↔client chat (TASK-313, schema from TASK-312).
/// Sessions/messages are visible to either tenant party via
/// tenant_isolation RLS (matches on SupplierTenantId OR ClientTenantId) — the
/// caller's own tenant id is supplied by TenantConnectionInterceptor, no
/// provider-bypass is required for either side of a conversation.
/// </summary>
public interface ISupplierChatRepository
{
    /// <summary>Finds the single persistent thread for a (SupplierTenantId, ClientTenantId) pair.</summary>
    Task<SupplierChatSession?> GetSessionAsync(
        Guid supplierTenantId, Guid clientTenantId, CancellationToken ct = default);

    Task<SupplierChatSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default);

    Task AddSessionAsync(SupplierChatSession session, CancellationToken ct = default);

    /// <summary>
    /// Lists sessions where the tenant is either the supplier or the client side,
    /// each joined with the other party's tenant display name and its most recent
    /// message (for a session-list preview), newest activity first.
    /// </summary>
    Task<IReadOnlyList<(SupplierChatSession Session, string OtherTenantName, SupplierChatMessage? LastMessage)>>
        GetSessionsAsync(Guid tenantId, bool isSupplierSide, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierChatMessage>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct = default);

    Task AddMessageAsync(SupplierChatMessage message, CancellationToken ct = default);

    Task<string?> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
