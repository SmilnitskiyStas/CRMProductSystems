using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Client → supplier support tickets (TASK-317). Deliberately NOT gated by a
/// cooperation agreement — any authenticated tenant may ask a supplier a
/// question. Message visibility follows the two-tenant RLS of the parent ticket.
/// </summary>
public sealed class SupplierSupportService : ISupplierSupportService
{
    public const string SupplierNotFoundError = "Постачальника не знайдено.";
    public const string TicketNotFoundError = "Тікет не знайдено.";
    public const string SubjectRequiredError = "Вкажіть тему звернення.";
    public const string BodyRequiredError = "Текст повідомлення не може бути порожнім.";
    public const string SelfTicketError = "Не можна створити тікет до власного постачальника.";

    public const int MaxSubjectLength = 300;
    public const int MaxBodyLength = 4000;

    private readonly ISupplierSupportTicketRepository _tickets;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _tenantNames;

    public SupplierSupportService(
        ISupplierSupportTicketRepository tickets,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames)
    {
        _tickets     = tickets;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
    }

    // ── Client side ───────────────────────────────────────────────────────────

    public async Task<(SupplierSupportTicketDto? Ticket, string? Error)> CreateTicketAsync(
        Guid clientTenantId, Guid supplierId, CreateSupportTicketDto request, Guid userId,
        CancellationToken ct = default)
    {
        var subject = request.Subject?.Trim();
        if (string.IsNullOrEmpty(subject))
            return (null, SubjectRequiredError);
        if (subject.Length > MaxSubjectLength)
            return (null, $"Тема не може перевищувати {MaxSubjectLength} символів.");

        var body = request.Message?.Trim();
        if (string.IsNullOrEmpty(body))
            return (null, BodyRequiredError);
        if (body.Length > MaxBodyLength)
            return (null, $"Повідомлення не може перевищувати {MaxBodyLength} символів.");

        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return (null, SupplierNotFoundError);

        if (supplierTenantId.Value == clientTenantId)
            return (null, SelfTicketError);

        var ticket = new SupplierSupportTicket
        {
            SupplierTenantId = supplierTenantId.Value,
            ClientTenantId   = clientTenantId,
            Subject          = subject,
            Status           = SupplierSupportTicketStatus.Open,
            CreatedByUserId  = userId,
        };

        var message = new SupplierSupportTicketMessage
        {
            TicketId       = ticket.Id,
            SenderTenantId = clientTenantId,
            SenderUserId   = userId,
            Body           = body,
        };
        ticket.Messages.Add(message);

        await _tickets.AddAsync(ticket, ct);
        await _tickets.SaveChangesAsync(ct);

        return (await ToDtoAsync(ticket, includeMessages: true, ct), null);
    }

    public async Task<IReadOnlyList<SupplierSupportTicketDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default)
    {
        var rows = await _tickets.ListForClientAsync(clientTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }

    // ── Both parties ──────────────────────────────────────────────────────────

    public async Task<(SupplierSupportTicketDto? Ticket, string? Error)> GetTicketAsync(
        Guid ticketId, Guid callerTenantId, CancellationToken ct = default)
    {
        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || !IsParty(ticket, callerTenantId))
            return (null, TicketNotFoundError);

        return (await ToDtoAsync(ticket, includeMessages: true, ct), null);
    }

    public async Task<(SupportTicketMessageDto? Message, string? Error)> AddMessageAsync(
        Guid ticketId, Guid callerTenantId, Guid callerUserId, string body,
        CancellationToken ct = default)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, BodyRequiredError);
        if (trimmed.Length > MaxBodyLength)
            return (null, $"Повідомлення не може перевищувати {MaxBodyLength} символів.");

        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || !IsParty(ticket, callerTenantId))
            return (null, TicketNotFoundError);

        var message = new SupplierSupportTicketMessage
        {
            TicketId       = ticket.Id,
            SenderTenantId = callerTenantId,
            SenderUserId   = callerUserId,
            Body           = trimmed,
        };

        await _tickets.AddMessageAsync(message, ct);

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        _tickets.Update(ticket);

        await _tickets.SaveChangesAsync(ct);

        return (ToMessageDto(message), null);
    }

    // ── Supplier side ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SupplierSupportTicketDto>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default)
    {
        var rows = await _tickets.ListForSupplierAsync(supplierTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<(SupplierSupportTicketDto? Ticket, string? Error)> UpdateStatusAsync(
        Guid supplierTenantId, Guid ticketId, string status, CancellationToken ct = default)
    {
        if (!SupplierSupportTicketStatus.All.Contains(status))
            return (null, $"Невідомий статус: '{status}'.");

        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.SupplierTenantId != supplierTenantId)
            return (null, TicketNotFoundError);

        ticket.Status    = status;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;

        _tickets.Update(ticket);
        await _tickets.SaveChangesAsync(ct);

        return (await ToDtoAsync(ticket, includeMessages: false, ct), null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsParty(SupplierSupportTicket ticket, Guid tenantId) =>
        ticket.SupplierTenantId == tenantId || ticket.ClientTenantId == tenantId;

    private async Task<IReadOnlyList<SupplierSupportTicketDto>> ToDtosAsync(
        IReadOnlyList<SupplierSupportTicket> rows, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        var result = new List<SupplierSupportTicketDto>(rows.Count);
        foreach (var row in rows)
            result.Add(ToDto(row,
                await GetNameCachedAsync(row.SupplierTenantId, names, ct),
                await GetNameCachedAsync(row.ClientTenantId, names, ct),
                includeMessages: false));
        return result;
    }

    private async Task<SupplierSupportTicketDto> ToDtoAsync(
        SupplierSupportTicket t, bool includeMessages, CancellationToken ct) =>
        ToDto(t,
            await _tenantNames.GetTenantDisplayNameAsync(t.SupplierTenantId, ct) ?? string.Empty,
            await _tenantNames.GetTenantDisplayNameAsync(t.ClientTenantId, ct) ?? string.Empty,
            includeMessages);

    private async Task<string> GetNameCachedAsync(
        Guid tenantId, Dictionary<Guid, string> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var name)) return name;
        name = await _tenantNames.GetTenantDisplayNameAsync(tenantId, ct) ?? string.Empty;
        cache[tenantId] = name;
        return name;
    }

    private static SupplierSupportTicketDto ToDto(
        SupplierSupportTicket t, string supplierName, string clientName, bool includeMessages) =>
        new(
            t.Id,
            t.SupplierTenantId,
            t.ClientTenantId,
            supplierName,
            clientName,
            t.Subject,
            t.Status,
            t.CreatedAt,
            t.UpdatedAt,
            includeMessages
                ? t.Messages.OrderBy(m => m.CreatedAt).Select(ToMessageDto).ToList()
                : null);

    private static SupportTicketMessageDto ToMessageDto(SupplierSupportTicketMessage m) =>
        new(m.Id, m.TicketId, m.SenderTenantId, m.SenderUserId, m.Body, m.IsRead, m.CreatedAt);
}
