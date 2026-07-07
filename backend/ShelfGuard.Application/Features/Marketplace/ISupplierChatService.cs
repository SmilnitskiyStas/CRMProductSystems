using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier↔client chat (TASK-313, calm-singing-marble Частина 2, schema TASK-312).
/// One persistent thread per (SupplierTenantId, ClientTenantId) pair. Either side
/// can initiate: the supplier from its "Clients" tab, or the client from the
/// supplier's public marketplace page. A single service serves both sides —
/// callers pass <c>isSupplierSide</c> to indicate which tenant column they occupy.
/// </summary>
public interface ISupplierChatService
{
    /// <summary>
    /// Finds the existing thread for (myTenantId, otherTenantId) — mapped onto
    /// (SupplierTenantId, ClientTenantId) according to <paramref name="isSupplierSide"/> —
    /// or creates a new one.
    /// </summary>
    Task<SupplierChatSessionDto> GetOrCreateSessionAsync(
        Guid myTenantId, Guid otherTenantId, bool isSupplierSide, Guid createdByUserId,
        CancellationToken ct = default);

    /// <summary>Lists all threads where the tenant occupies the given side.</summary>
    Task<IReadOnlyList<SupplierChatSessionDto>> GetSessionsAsync(
        Guid tenantId, bool isSupplierSide, CancellationToken ct = default);

    /// <summary>
    /// Returns the messages of a session. Returns an error if the calling tenant is
    /// neither the SupplierTenantId nor the ClientTenantId of the session. As a side
    /// effect, marks all of the OTHER party's messages in this session as read
    /// (TASK-319) — callers polling this endpoint naturally clear unread counts.
    /// </summary>
    Task<(IReadOnlyList<SupplierChatMessageDto>? Messages, string? Error)> GetMessagesAsync(
        Guid sessionId, Guid callerTenantId, CancellationToken ct = default);

    /// <summary>
    /// Posts a new message. Validates that the body is non-empty and within the
    /// 4000-character entity limit, and that the caller's tenant belongs to the session.
    /// </summary>
    Task<(SupplierChatMessageDto? Message, string? Error)> SendMessageAsync(
        Guid sessionId, Guid senderTenantId, Guid senderUserId, string senderName, string body,
        CancellationToken ct = default);
}
