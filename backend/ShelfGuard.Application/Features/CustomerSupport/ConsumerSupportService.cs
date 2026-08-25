using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.CustomerSupport.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.CustomerSupport;

/// <summary>See <see cref="IConsumerSupportService"/> for the responsibility split.</summary>
public sealed class ConsumerSupportService : IConsumerSupportService
{
    public const string ConsumerNotFoundError = "Consumer account not found.";
    public const string TenantNotFoundError = "Tenant not found.";
    public const string TicketNotFoundError = "Ticket not found.";
    public const string SubjectRequiredError = "Subject is required.";
    public const string BodyRequiredError = "Message cannot be empty.";

    public const int MaxSubjectLength = 500;
    public const int MaxBodyLength = 4000;

    private readonly IConsumerSupportTicketRepository _tickets;
    private readonly IConsumerAccountRepository _consumerAccounts;
    private readonly ICustomerRepository _customers;
    private readonly ILoyaltyRepository _loyalty;
    private readonly ITenantRepository _tenants;
    private readonly ITenantSessionOverride _tenantScope;
    private readonly IConsumerSupportRealtimeNotifier _realtime;
    private readonly ILogger<ConsumerSupportService> _logger;

    public ConsumerSupportService(
        IConsumerSupportTicketRepository tickets,
        IConsumerAccountRepository consumerAccounts,
        ICustomerRepository customers,
        ILoyaltyRepository loyalty,
        ITenantRepository tenants,
        ITenantSessionOverride tenantScope,
        IConsumerSupportRealtimeNotifier realtime,
        ILogger<ConsumerSupportService> logger)
    {
        _tickets = tickets;
        _consumerAccounts = consumerAccounts;
        _customers = customers;
        _loyalty = loyalty;
        _tenants = tenants;
        _tenantScope = tenantScope;
        _realtime = realtime;
        _logger = logger;
    }

    // ── Consumer side ─────────────────────────────────────────────────────────

    public async Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> CreateTicketAsync(
        Guid consumerAccountId, Guid tenantId, string subject, string body, CancellationToken ct = default)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, ConsumerNotFoundError, 404);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, TenantNotFoundError, 404);

        var trimmedSubject = subject?.Trim();
        if (string.IsNullOrEmpty(trimmedSubject))
            return (null, SubjectRequiredError, 400);
        if (trimmedSubject.Length > MaxSubjectLength)
            return (null, $"Subject cannot exceed {MaxSubjectLength} characters.", 400);

        var trimmedBody = body?.Trim();
        if (string.IsNullOrEmpty(trimmedBody))
            return (null, BodyRequiredError, 400);
        if (trimmedBody.Length > MaxBodyLength)
            return (null, $"Message cannot exceed {MaxBodyLength} characters.", 400);

        var customerId = await ResolveCustomerIdAsync(tenantId, consumerAccountId, consumer.Phone, ct);

        var ticket = new ConsumerSupportTicket
        {
            TenantId = tenantId,
            ConsumerAccountId = consumerAccountId,
            CustomerId = customerId,
            Subject = trimmedSubject,
            Status = ConsumerSupportTicketStatus.Open,
        };

        var message = new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderConsumerAccountId = consumerAccountId,
            Body = trimmedBody,
        };
        ticket.Messages.Add(message);

        // consumer_support_tickets' consumer_self_access RLS policy (USING, doubling as the
        // implicit WITH CHECK) already lets this consumer session insert a row with its own
        // ConsumerAccountId — no ITenantSessionOverride needed here, same reasoning
        // LoyaltyService.JoinAsync's own doc gives for why LoyaltyMembership's insert would
        // already succeed on consumer_self_access alone.
        await _tickets.AddAsync(ticket, ct);
        await _tickets.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Consumer {ConsumerId} opened support ticket {TicketId} for tenant {TenantId}.",
            consumerAccountId, ticket.Id, tenantId);

        return (await ToDtoForConsumerAsync(ticket, includeMessages: true, ct), null, null);
    }

    public async Task<PagedResult<ConsumerSupportTicketDto>> GetMyTicketsAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _tickets.GetPagedForConsumerAsync(
            consumerAccountId, tenantId, clampedPage, clampedPageSize, ct);

        return new PagedResult<ConsumerSupportTicketDto>
        {
            Items = await ToDtosForConsumerAsync(items, ct),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
    }

    public async Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> GetTicketAsync(
        Guid consumerAccountId, Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.ConsumerAccountId != consumerAccountId)
            return (null, TicketNotFoundError, 404); // uniform 404 — never discloses another consumer's ticket exists

        return (await ToDtoForConsumerAsync(ticket, includeMessages: true, ct), null, null);
    }

    public async Task<(ConsumerSupportTicketMessageDto? Message, string? Error, int? StatusCode)> AddConsumerMessageAsync(
        Guid consumerAccountId, Guid ticketId, string body, CancellationToken ct = default)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, BodyRequiredError, 400);
        if (trimmed.Length > MaxBodyLength)
            return (null, $"Message cannot exceed {MaxBodyLength} characters.", 400);

        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.ConsumerAccountId != consumerAccountId)
            return (null, TicketNotFoundError, 404);

        var message = new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderConsumerAccountId = consumerAccountId,
            Body = trimmed,
        };
        await _tickets.AddMessageAsync(message, ct);

        ticket.UpdatedAt = DateTimeOffset.UtcNow;

        // Judgment call (TASK-616, no explicit product decision on file): a consumer replying
        // after staff had marked the ticket Resolved/Closed reopens it to Open. Staff closing a
        // ticket is otherwise sticky (nothing else in this service flips status automatically),
        // but the customer's own follow-up is a clear signal the issue isn't actually settled
        // from their side — silently dropping it into a closed thread would bury it from staff.
        if (ticket.Status is ConsumerSupportTicketStatus.Resolved or ConsumerSupportTicketStatus.Closed)
            ticket.Status = ConsumerSupportTicketStatus.Open;

        _tickets.Update(ticket);
        await _tickets.SaveChangesAsync(ct);

        // TASK-625: realtime event — post-commit only (SaveChangesAsync above already
        // succeeded), best-effort (the notifier swallows its own failures, see its doc).
        // Deliberately NOT publishing SupportTicketStatusChanged for the reopen-on-reply side
        // effect above — spec §4 ties that event exclusively to the PUT .../status endpoint;
        // a reconnecting client picks up an implicit reopen via its own GET refetch instead.
        var dto = ToMessageDto(message);
        await _realtime.MessageCreatedAsync(ticket.Id, dto, ct);

        return (dto, null, null);
    }

    // ── Staff side ────────────────────────────────────────────────────────────

    public async Task<PagedResult<ConsumerSupportTicketDto>> GetInboxAsync(
        Guid tenantId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _tickets.GetPagedForTenantAsync(
            tenantId, status, clampedPage, clampedPageSize, ct);

        return new PagedResult<ConsumerSupportTicketDto>
        {
            Items = await ToDtosForStaffAsync(items, ct),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
    }

    public async Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> GetTicketForStaffAsync(
        Guid tenantId, Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.TenantId != tenantId)
            return (null, TicketNotFoundError, 404);

        // Side effect: opening a ticket on the staff side marks every unread consumer message
        // read. GetByIdAsync's query is tracked (no AsNoTracking), so mutating Messages here and
        // calling SaveChangesAsync is enough — no separate repository.Update call needed.
        var unread = ticket.Messages.Where(m => m.SenderConsumerAccountId is not null && !m.IsRead).ToList();
        if (unread.Count > 0)
        {
            foreach (var message in unread)
                message.IsRead = true;
            await _tickets.SaveChangesAsync(ct);
        }

        return (await ToDtoForStaffAsync(ticket, includeMessages: true, ct), null, null);
    }

    public async Task<(ConsumerSupportTicketMessageDto? Message, string? Error, int? StatusCode)> AddStaffReplyAsync(
        Guid tenantId, Guid ticketId, Guid staffUserId, string body, CancellationToken ct = default)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, BodyRequiredError, 400);
        if (trimmed.Length > MaxBodyLength)
            return (null, $"Message cannot exceed {MaxBodyLength} characters.", 400);

        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.TenantId != tenantId)
            return (null, TicketNotFoundError, 404);

        var message = new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderUserId = staffUserId,
            Body = trimmed,
        };
        await _tickets.AddMessageAsync(message, ct);

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        _tickets.Update(ticket);
        await _tickets.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Staff user {UserId} replied on support ticket {TicketId} (tenant {TenantId}).",
            staffUserId, ticket.Id, tenantId);

        // TASK-625: realtime event — post-commit only, best-effort (see AddConsumerMessageAsync).
        var dto = ToMessageDto(message);
        await _realtime.MessageCreatedAsync(ticket.Id, dto, ct);

        return (dto, null, null);
    }

    public async Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> UpdateStatusAsync(
        Guid tenantId, Guid ticketId, Guid staffUserId, string newStatus, CancellationToken ct = default)
    {
        if (!ConsumerSupportTicketStatus.All.Contains(newStatus))
            return (null, $"Unknown status: '{newStatus}'.", 400);

        var ticket = await _tickets.GetByIdAsync(ticketId, ct);
        if (ticket is null || ticket.TenantId != tenantId)
            return (null, TicketNotFoundError, 404);

        ticket.Status = newStatus;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        _tickets.Update(ticket);
        await _tickets.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Staff user {UserId} set support ticket {TicketId} (tenant {TenantId}) to status {Status}.",
            staffUserId, ticket.Id, tenantId, newStatus);

        // TASK-625: realtime event — post-commit only, best-effort (see AddConsumerMessageAsync).
        await _realtime.StatusChangedAsync(ticket.Id, ticket.Status, ticket.UpdatedAt, ct);

        return (await ToDtoForStaffAsync(ticket, includeMessages: false, ct), null, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort CustomerId auto-link, reusing the two lookups that already resolve a CRM
    /// <see cref="Customer"/> from a ConsumerAccount+tenant elsewhere in the codebase — no new
    /// linking mechanism is introduced here:
    /// 1. If this consumer already has a <see cref="LoyaltyMembership"/> at this tenant, reuse
    ///    its own already-resolved <see cref="LoyaltyMembership.CustomerId"/> directly (that
    ///    membership was itself created via the same phone-match FindOrCreateCustomerAsync path
    ///    LoyaltyService uses — see LoyaltyService.FindOrCreateCustomerAsync).
    /// 2. Otherwise, fall back to the same phone-match <see cref="ICustomerRepository.FindByPhoneAsync"/>
    ///    LoyaltyService itself uses to auto-link (see that method's own doc) — but never CREATE
    ///    a Customer here (unlike Loyalty's find-or-create): the ticket's own doc says "when one
    ///    exists", and opening a support ticket is not consent to create a new CRM record.
    /// "customers" carries no consumer_self_access RLS policy (unlike loyalty_memberships), so
    /// this fallback path needs the same <see cref="ITenantSessionOverride"/> LoyaltyService uses
    /// for its own consumer-session reads/writes against that table.
    /// </summary>
    private async Task<Guid?> ResolveCustomerIdAsync(
        Guid tenantId, Guid consumerAccountId, string phone, CancellationToken ct)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership?.CustomerId is Guid linkedCustomerId)
            return linkedCustomerId;

        var customer = await _tenantScope.ExecuteAsync(
            tenantId, () => _customers.FindByPhoneAsync(phone, tenantId, ct), ct);
        return customer?.Id;
    }

    /// <summary>Consumer-session DTO conversion for a single ticket — resolves CustomerName (when
    /// linked) through <see cref="ITenantSessionOverride"/>, same reasoning as
    /// <see cref="ResolveCustomerIdAsync"/>'s own fallback lookup.</summary>
    private async Task<ConsumerSupportTicketDto> ToDtoForConsumerAsync(
        ConsumerSupportTicket t, bool includeMessages, CancellationToken ct)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(t.ConsumerAccountId, ct);
        string? customerName = null;
        if (t.CustomerId is Guid customerId)
        {
            var customer = await _tenantScope.ExecuteAsync(
                t.TenantId, () => _customers.GetByIdAsync(customerId, t.TenantId, ct), ct);
            customerName = customer?.Name;
        }
        return ToDto(t, consumer?.FullName ?? "—", consumer?.Phone ?? "—", customerName, includeMessages);
    }

    /// <summary>List variant of <see cref="ToDtoForConsumerAsync"/> — always exactly one
    /// consumer's tickets, so consumer name/phone is resolved once and reused for every row.</summary>
    private async Task<List<ConsumerSupportTicketDto>> ToDtosForConsumerAsync(
        IReadOnlyList<ConsumerSupportTicket> rows, CancellationToken ct)
    {
        var result = new List<ConsumerSupportTicketDto>(rows.Count);
        if (rows.Count == 0) return result;

        var consumer = await _consumerAccounts.GetByIdAsync(rows[0].ConsumerAccountId, ct);
        var customerNameCache = new Dictionary<Guid, string?>();

        foreach (var row in rows)
        {
            string? customerName = null;
            if (row.CustomerId is Guid customerId)
            {
                if (!customerNameCache.TryGetValue(customerId, out customerName))
                {
                    var customer = await _tenantScope.ExecuteAsync(
                        row.TenantId, () => _customers.GetByIdAsync(customerId, row.TenantId, ct), ct);
                    customerName = customer?.Name;
                    customerNameCache[customerId] = customerName;
                }
            }
            result.Add(ToDto(row, consumer?.FullName ?? "—", consumer?.Phone ?? "—", customerName, includeMessages: false));
        }
        return result;
    }

    /// <summary>Staff-session DTO conversion for a single ticket — the calling session already
    /// carries a real app.tenant_id for this tenant, so CustomerName is resolved directly, no
    /// <see cref="ITenantSessionOverride"/> needed (unlike the consumer-facing variant above).</summary>
    private async Task<ConsumerSupportTicketDto> ToDtoForStaffAsync(
        ConsumerSupportTicket t, bool includeMessages, CancellationToken ct)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(t.ConsumerAccountId, ct);
        var customerName = t.CustomerId is Guid customerId
            ? (await _customers.GetByIdAsync(customerId, t.TenantId, ct))?.Name
            : null;
        return ToDto(t, consumer?.FullName ?? "—", consumer?.Phone ?? "—", customerName, includeMessages);
    }

    /// <summary>List variant of <see cref="ToDtoForStaffAsync"/> — caches both consumer and
    /// customer name lookups per request, mirroring SupplierSupportService.ToDtosAsync's own
    /// name-cache pattern (an inbox page can span many different consumers).</summary>
    private async Task<List<ConsumerSupportTicketDto>> ToDtosForStaffAsync(
        IReadOnlyList<ConsumerSupportTicket> rows, CancellationToken ct)
    {
        var consumerCache = new Dictionary<Guid, (string Name, string Phone)>();
        var customerCache = new Dictionary<Guid, string?>();
        var result = new List<ConsumerSupportTicketDto>(rows.Count);

        foreach (var row in rows)
        {
            if (!consumerCache.TryGetValue(row.ConsumerAccountId, out var consumerInfo))
            {
                var consumer = await _consumerAccounts.GetByIdAsync(row.ConsumerAccountId, ct);
                consumerInfo = (consumer?.FullName ?? "—", consumer?.Phone ?? "—");
                consumerCache[row.ConsumerAccountId] = consumerInfo;
            }

            string? customerName = null;
            if (row.CustomerId is Guid customerId)
            {
                if (!customerCache.TryGetValue(customerId, out customerName))
                {
                    var customer = await _customers.GetByIdAsync(customerId, row.TenantId, ct);
                    customerName = customer?.Name;
                    customerCache[customerId] = customerName;
                }
            }

            result.Add(ToDto(row, consumerInfo.Name, consumerInfo.Phone, customerName, includeMessages: false));
        }
        return result;
    }

    private static ConsumerSupportTicketDto ToDto(
        ConsumerSupportTicket t, string consumerName, string consumerPhone, string? customerName, bool includeMessages) =>
        new(
            t.Id,
            t.TenantId,
            t.ConsumerAccountId,
            consumerName,
            consumerPhone,
            t.CustomerId,
            customerName,
            t.Subject,
            t.Status,
            t.CreatedAt,
            t.UpdatedAt,
            includeMessages
                ? t.Messages.OrderBy(m => m.CreatedAt).Select(ToMessageDto).ToList()
                : null);

    private static ConsumerSupportTicketMessageDto ToMessageDto(ConsumerSupportTicketMessage m) =>
        new(m.Id, m.TicketId, m.SenderConsumerAccountId, m.SenderUserId, m.Body, m.IsRead, m.CreatedAt);
}
