using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Chat;
using ShelfGuard.Application.Features.Chat.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

public sealed class ChatService : IChatService
{
    private readonly AppDbContext _db;

    public ChatService(AppDbContext db) => _db = db;

    // ── Tenant: Create session ────────────────────────────────────────────────

    public async Task<ChatSessionDto> CreateSessionAsync(
        Guid tenantId, Guid userId, string userName,
        CreateChatSessionRequest req, CancellationToken ct)
    {
        var session = new ChatSession
        {
            TenantId        = tenantId,
            CreatedByUserId = userId,
            Subject         = req.Subject,
            Status          = "open",
        };

        var firstMessage = new ChatMessage
        {
            SessionId    = session.Id,
            SenderUserId = userId,
            SenderName   = userName,
            Body         = req.FirstMessage,
            IsRead       = false,
        };

        session.Messages.Add(firstMessage);

        await _db.ChatSessions.AddAsync(session, ct);
        await _db.SaveChangesAsync(ct);

        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return ToSessionDto(session, tenantName, unreadCount: 0);
    }

    // ── Tenant: List sessions ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(
        Guid tenantId, CancellationToken ct)
    {
        var sessions = await _db.ChatSessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Include(s => s.Messages)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return sessions
            .Select(s => ToSessionDto(s, tenantName, unreadCount: 0))
            .ToList();
    }

    // ── Tenant: Get messages ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid sessionId, Guid tenantId, CancellationToken ct)
    {
        var messages = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId && m.Session!.TenantId == tenantId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return messages.Select(ToMessageDto).ToList();
    }

    // ── Tenant: Send message ──────────────────────────────────────────────────

    public async Task<ChatMessageDto> SendMessageAsync(
        Guid sessionId, Guid tenantId, Guid userId, string userName,
        SendChatMessageRequest req, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("Session not found.");

        var message = new ChatMessage
        {
            SessionId    = sessionId,
            SenderUserId = userId,
            SenderName   = userName,
            Body         = req.Body,
            IsRead       = false,
        };

        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.ChatMessages.AddAsync(message, ct);
        await _db.SaveChangesAsync(ct);

        return ToMessageDto(message);
    }

    // ── Tenant: Close session ─────────────────────────────────────────────────

    public async Task CloseSessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("Session not found.");

        session.Status    = "closed";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Provider: Get all sessions (cross-tenant) ─────────────────────────────

    public async Task<IReadOnlyList<ChatSessionDto>> GetAllSessionsForProviderAsync(CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ''", ct);

        var rows = await _db.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .Join(
                _db.Tenants,
                s => s.TenantId,
                t => t.Id,
                (s, t) => new { Session = s, TenantName = t.Name })
            .OrderByDescending(r => r.Session.UpdatedAt)
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var unread = r.Session.Messages.Count(m => !m.IsRead);
                return ToSessionDto(r.Session, r.TenantName, unread);
            })
            .ToList();
    }

    // ── Provider: Get messages for a session (cross-tenant) ───────────────────

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesForProviderAsync(
        Guid sessionId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ''", ct);

        var messages = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Mark all unread messages as read (provider opened the session)
        var unreadIds = messages.Where(m => !m.IsRead).Select(m => m.Id).ToList();
        if (unreadIds.Count > 0)
        {
            await _db.ChatMessages
                .Where(m => unreadIds.Contains(m.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct);
        }

        return messages.Select(ToMessageDto).ToList();
    }

    // ── Provider: Send message (cross-tenant) ─────────────────────────────────

    public async Task<ChatMessageDto> ProviderSendMessageAsync(
        Guid sessionId, Guid providerId, string providerName,
        SendChatMessageRequest req, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ''", ct);

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found.");

        var message = new ChatMessage
        {
            SessionId    = sessionId,
            SenderUserId = providerId,
            SenderName   = providerName,
            Body         = req.Body,
            IsRead       = true, // provider's own message is already "read"
        };

        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.ChatMessages.AddAsync(message, ct);
        await _db.SaveChangesAsync(ct);

        return ToMessageDto(message);
    }

    // ── Provider: Close session (cross-tenant) ────────────────────────────────

    public async Task ProviderCloseSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ''", ct);

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found.");

        session.Status    = "closed";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static ChatSessionDto ToSessionDto(ChatSession s, string tenantName, int unreadCount)
    {
        var lastMessage = s.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Select(ToMessageDto)
            .FirstOrDefault();

        return new ChatSessionDto(
            Id:          s.Id,
            TenantId:    s.TenantId,
            TenantName:  tenantName,
            Subject:     s.Subject,
            Status:      s.Status,
            UnreadCount: unreadCount,
            CreatedAt:   s.CreatedAt,
            UpdatedAt:   s.UpdatedAt,
            LastMessage: lastMessage
        );
    }

    private static ChatMessageDto ToMessageDto(ChatMessage m) =>
        new(
            Id:           m.Id,
            SessionId:    m.SessionId,
            SenderUserId: m.SenderUserId,
            SenderName:   m.SenderName,
            Body:         m.Body,
            IsRead:       m.IsRead,
            CreatedAt:    m.CreatedAt
        );
}
