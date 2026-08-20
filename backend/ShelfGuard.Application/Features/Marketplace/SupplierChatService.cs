using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Services;
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

    private const int TitleExcerptLength = 80;

    private readonly ISupplierChatRepository _repo;
    private readonly INotificationRepository _notifications;
    private readonly ITenantSessionOverride _tenantSessionOverride;

    public SupplierChatService(
        ISupplierChatRepository repo, INotificationRepository notifications, ITenantSessionOverride tenantSessionOverride)
    {
        _repo = repo;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
    }

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
        return ToSessionDto(session, otherTenantId, otherTenantName, null, 0);
    }

    public async Task<IReadOnlyList<SupplierChatSessionDto>> GetSessionsAsync(
        Guid tenantId, bool isSupplierSide, CancellationToken ct = default)
    {
        var rows = await _repo.GetSessionsAsync(tenantId, isSupplierSide, ct);
        return rows
            .Select(r =>
            {
                var otherTenantId = isSupplierSide ? r.Session.ClientTenantId : r.Session.SupplierTenantId;
                return ToSessionDto(r.Session, otherTenantId, r.OtherTenantName, r.LastMessage, r.UnreadCount);
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

        // Opening/polling a thread marks the other party's messages as read.
        await _repo.MarkMessagesReadAsync(sessionId, callerTenantId, ct);

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

        // ADR-018 §2: only supplier → client messages notify (the client isn't otherwise
        // watching this thread); client → supplier messages don't enqueue anything here.
        if (session.SupplierTenantId == senderTenantId)
        {
            // TASK-582: EnqueueSupplierMessageNotificationAsync inserts into notification_queue
            // with TenantId = session.ClientTenantId (the recipient), while the ambient DB
            // session here is authenticated as the SUPPLIER tenant (the sender) —
            // notification_queue's plain tenant_isolation RLS policy only allows TenantId =
            // session tenant, so an unscoped insert here would throw an unhandled
            // PostgresException (42501), same bug class as SupplierAgreementService.MarkSignedAsync
            // (TASK-582). Run the notification enqueue and the final SaveChangesAsync under an
            // explicit override of the CLIENT tenant's RLS context (ITenantSessionOverride) —
            // safe because session.ClientTenantId is already a trusted value (this session was
            // already loaded and the caller confirmed above to belong to it), and
            // supplier_chat_messages'/supplier_chat_sessions' own RLS is OR-based on both
            // SupplierTenantId/ClientTenantId, so the chat message insert and session bump that
            // also get flushed by this same SaveChangesAsync call still satisfy their RLS under
            // either tenant. The client → supplier branch below doesn't need this — it never
            // enqueues, and already writes fine under the sender's own (client) tenant.
            await _tenantSessionOverride.ExecuteAsync(session.ClientTenantId, async () =>
            {
                await EnqueueSupplierMessageNotificationAsync(session, message, ct);
                await _repo.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        else
        {
            await _repo.SaveChangesAsync(ct);
        }

        return (ToMessageDto(message), null);
    }

    /// <summary>
    /// ADR-018 §2: Postgres outbox row (EventType = "supplier.message") for the client
    /// tenant, picked up by the worker's notification-dispatch job. UserId = null,
    /// Channel = "system", Status = "pending".
    /// </summary>
    private async Task EnqueueSupplierMessageNotificationAsync(
        SupplierChatSession session, SupplierChatMessage message, CancellationToken ct)
    {
        var excerpt = message.Body.Length > TitleExcerptLength
            ? message.Body[..TitleExcerptLength] + "…"
            : message.Body;

        var payload = JsonSerializer.Serialize(new
        {
            sessionId = session.Id,
            supplierTenantId = session.SupplierTenantId,
            senderName = message.SenderName,
            excerpt,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = session.ClientTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Повідомлення від {message.SenderName}: {excerpt}",
            Channel   = "system",
            EventType = "supplier.message",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    private static SupplierChatSessionDto ToSessionDto(
        SupplierChatSession session, Guid otherTenantId, string otherTenantName, SupplierChatMessage? lastMessage,
        int unreadCount) =>
        new(
            session.Id,
            otherTenantId,
            otherTenantName,
            session.CreatedAt,
            session.UpdatedAt,
            lastMessage?.Body,
            lastMessage?.CreatedAt,
            unreadCount);

    private static SupplierChatMessageDto ToMessageDto(SupplierChatMessage m) =>
        new(m.Id, m.SessionId, m.SenderTenantId, m.SenderUserId, m.SenderName, m.Body, m.IsRead, m.CreatedAt);
}
