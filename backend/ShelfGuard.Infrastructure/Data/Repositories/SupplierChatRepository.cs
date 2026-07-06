using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the supplier↔client chat (TASK-313). Standard tenant RLS
/// applies via TenantConnectionInterceptor — the tenant_isolation policy on
/// supplier_chat_sessions matches either SupplierTenantId or ClientTenantId
/// against app.tenant_id, so either party's tenant context can read/write its
/// own sessions/messages without a provider-role bypass.
/// </summary>
public sealed class SupplierChatRepository : ISupplierChatRepository
{
    private readonly AppDbContext _db;

    public SupplierChatRepository(AppDbContext db) => _db = db;

    public Task<SupplierChatSession?> GetSessionAsync(
        Guid supplierTenantId, Guid clientTenantId, CancellationToken ct = default) =>
        _db.SupplierChatSessions.FirstOrDefaultAsync(
            s => s.SupplierTenantId == supplierTenantId && s.ClientTenantId == clientTenantId, ct);

    public Task<SupplierChatSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        _db.SupplierChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task AddSessionAsync(SupplierChatSession session, CancellationToken ct = default) =>
        await _db.SupplierChatSessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<(SupplierChatSession Session, string OtherTenantName, SupplierChatMessage? LastMessage)>>
        GetSessionsAsync(Guid tenantId, bool isSupplierSide, CancellationToken ct = default)
    {
        var query = isSupplierSide
            ? _db.SupplierChatSessions.AsNoTracking().Where(s => s.SupplierTenantId == tenantId)
            : _db.SupplierChatSessions.AsNoTracking().Where(s => s.ClientTenantId == tenantId);

        var sessions = await query.ToListAsync(ct);
        if (sessions.Count == 0)
            return Array.Empty<(SupplierChatSession, string, SupplierChatMessage?)>();

        var otherTenantIds = sessions
            .Select(s => isSupplierSide ? s.ClientTenantId : s.SupplierTenantId)
            .Distinct()
            .ToList();

        var names = await _db.Tenants
            .AsNoTracking()
            .Where(t => otherTenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var lastMessages = await _db.SupplierChatMessages
            .AsNoTracking()
            .Where(m => sessionIds.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToListAsync(ct);
        var lastMessageBySession = lastMessages.ToDictionary(m => m.SessionId);

        return sessions
            .Select(s =>
            {
                var otherTenantId = isSupplierSide ? s.ClientTenantId : s.SupplierTenantId;
                var otherName = names.TryGetValue(otherTenantId, out var n) ? n : string.Empty;
                lastMessageBySession.TryGetValue(s.Id, out var lastMessage);
                return (s, otherName, lastMessage);
            })
            .OrderByDescending(x => x.lastMessage?.CreatedAt ?? x.s.UpdatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<SupplierChatMessage>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct = default) =>
        await _db.SupplierChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task AddMessageAsync(SupplierChatMessage message, CancellationToken ct = default) =>
        await _db.SupplierChatMessages.AddAsync(message, ct);

    public Task<string?> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (string?)t.Name).FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
