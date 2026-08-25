namespace ShelfGuard.Application.Features.CustomerSupport.Dtos;

/// <summary>
/// Consumer ↔ tenant support ticket (TASK-616). Same DTO is returned to both parties — mirrors
/// <c>SupplierSupportTicketDto</c>'s shape. <see cref="Messages"/> is null on list endpoints,
/// populated (oldest first) only on the single-ticket detail read.
/// </summary>
public sealed record ConsumerSupportTicketDto(
    Guid Id,
    Guid TenantId,
    Guid ConsumerAccountId,
    string ConsumerName,
    string ConsumerPhone,
    Guid? CustomerId,
    string? CustomerName,
    string Subject,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConsumerSupportTicketMessageDto>? Messages = null);

/// <summary>
/// One message in a <see cref="ConsumerSupportTicketDto"/>'s thread. Exactly one of
/// <see cref="SenderConsumerAccountId"/>/<see cref="SenderUserId"/> is set — mirrors the entity,
/// letting the client derive "mine vs. theirs" the same way SupportTicketMessageDto does.
/// </summary>
public sealed record ConsumerSupportTicketMessageDto(
    Guid Id,
    Guid TicketId,
    Guid? SenderConsumerAccountId,
    Guid? SenderUserId,
    string Body,
    bool IsRead,
    DateTimeOffset CreatedAt);

/// <summary>
/// POST /api/consumer/support/tickets. Carries <see cref="TenantId"/> in the body (rather than
/// the route) since a consumer session is cross-tenant by design — same shape as
/// ConsumerLoyaltyController's <c>SetPreferredStoreRequest</c>.
/// </summary>
public sealed record CreateConsumerSupportTicketRequest(Guid TenantId, string Subject, string Body);

/// <summary>POST /api/consumer/support/tickets/{id}/messages.</summary>
public sealed record AddConsumerSupportTicketMessageRequest(string Body);

/// <summary>POST /api/customer-support/tickets/{id}/reply.</summary>
public sealed record AddStaffSupportReplyRequest(string Body);

/// <summary>PUT /api/customer-support/tickets/{id}/status. Status ∈ open | in_progress | resolved | closed.</summary>
public sealed record UpdateConsumerSupportTicketStatusRequest(string Status);
