using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier↔client chat (TASK-313). One persistent thread per (SupplierTenantId,
/// ClientTenantId) pair — "get or create" upserts on that unique pair rather than
/// creating a new row per conversation. No Status/close concept (TASK-312 schema).
/// </summary>
public sealed class SupplierChatService : ISupplierChatService
{
    public const int MaxBodyLength = 4000;
    public const string SessionNotFoundError = "Chat session not found.";
    public const string AccessDeniedError = "You do not have access to this chat session.";
    public const string BodyRequiredError = "Message body is required.";

    private readonly ISupplierChatRepository _repo;

    public SupplierChatService(ISupplierChatRepository repo) => _repo = repo;

    public async Task<SupplierChatSessionDto> GetOrCreateSessionAsync(
        Guid myTenantId, Guid otherTenantId, bool isSupplierSide, Guid createdByUserId,
        CancellationToken ct = default)
    {
        var supplierTenantId = isSupplierSide ? myTenantId : otherTenantId;
        var clientTenantId   = isSupplierSide ? otherTenantId : myTenantId;

        var session = await _repo.GetSessionAsync(supplierTenantId, clientTenantId, ct);

        if (session is null)
        {
            session = new SupplierChatSession
            {
                SupplierTenantId = supplierTenantId,
                ClientTenantId   = clientTenantId,
                CreatedByUserId  = createdByUserId,
            };

            await _repo.AddSessionAsync(session, ct);

            try
            {
                await _repo.SaveChangesAsync(ct);
            }
            catch (Exception)
            {
                // Lost a race on the unique (SupplierTenantId, ClientTenantId) index —
                // another request created the pair first. Re-fetch the winner.
                var existing = await _repo.GetSessionAsync(supplierTenantId, clientTenantId, ct);
                if (existing is null) throw;
                session = existing;
            }
        }

        var otherTenantName = await _repo.GetTenantDisplayNameAsync(otherTenantId, ct) ?? string.Empty;
        return ToSessionDto(session, otherTenantId, otherTenantName, null);
    }

    public async Task<IReadOnlyList<SupplierChatSessionDto>> GetSessionsAsync(
        Guid tenantId, bool isSupplierSide, CancellationToken ct = default)
    {
        var rows = await _repo.GetSessionsAsync(tenantId, isSupplierSide, ct);
        return rows
            .Select(r =>
            {
                var otherTenantId = isSupplierSide ? r.Session.ClientTenantId : r.Session.SupplierTenantId;
                return ToSessionDto(r.Session, otherTenantId, r.OtherTenantName, r.LastMessage);
            })
            .ToList();
    }

    public async Task<(IReadOnlyList<SupplierChatMessageDto>? Messages, string? Error)> GetMessagesAsync(
        Guid sessionId, Guid callerTenantId, CancellationToken ct = default)
    {
        var session = await _repo.GetSessionByIdAsync(sessionId, ct);
        if (session is null) return (null, SessionNotFoundError);

        if (session.SupplierTenantId != callerTenantId && session.ClientTenantId != callerTenantId)
            return (null, AccessDeniedError);

        var messages = await _repo.GetMessagesAsync(sessionId, ct);
        return (messages.Select(ToMessageDto).ToList(), null);
    }

    public async Task<(SupplierChatMessageDto? Message, string? Error)> SendMessageAsync(
        Guid sessionId, Guid senderTenantId, Guid senderUserId, string senderName, string body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, BodyRequiredError);

        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
            return (null, $"Message body cannot exceed {MaxBodyLength} characters.");

        var session = await _repo.GetSessionByIdAsync(sessionId, ct);
        if (session is null) return (null, SessionNotFoundError);

        if (session.SupplierTenantId != senderTenantId && session.ClientTenantId != senderTenantId)
            return (null, AccessDeniedError);

        var message = new SupplierChatMessage
        {
            SessionId      = sessionId,
            SenderTenantId = senderTenantId,
            SenderUserId   = senderUserId,
            SenderName     = senderName,
            Body           = trimmed,
        };

        await _repo.AddMessageAsync(message, ct);

        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(ct);

        return (ToMessageDto(message), null);
    }

    private static SupplierChatSessionDto ToSessionDto(
        SupplierChatSession session, Guid otherTenantId, string otherTenantName, SupplierChatMessage? lastMessage) =>
        new(
            session.Id,
            otherTenantId,
            otherTenantName,
            session.CreatedAt,
            session.UpdatedAt,
            lastMessage?.Body,
            lastMessage?.CreatedAt);

    private static SupplierChatMessageDto ToMessageDto(SupplierChatMessage m) =>
        new(m.Id, m.SessionId, m.SenderTenantId, m.SenderUserId, m.SenderName, m.Body, m.IsRead, m.CreatedAt);
}
