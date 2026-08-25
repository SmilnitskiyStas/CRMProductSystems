namespace ShelfGuard.Application.Features.Reviews.Dtos;

/// <summary>
/// A consumer's review of one completed purchase (TASK-617) — same DTO returned to both the
/// reviewing consumer and staff, mirrors <c>ConsumerSupportTicketDto</c>'s "one shape, two
/// audiences" convention.
/// </summary>
public sealed record PurchaseReviewDto(
    Guid Id,
    Guid TenantId,
    Guid ConsumerAccountId,
    string ConsumerName,
    string ConsumerPhone,
    Guid PosTransactionId,
    short Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    string? ReplyText,
    DateTimeOffset? RepliedAt,
    Guid? RepliedByUserId);

/// <summary>
/// POST /api/consumer/reviews. Carries <see cref="TenantId"/> in the body (rather than the
/// route) since a consumer session is cross-tenant by design — same shape as
/// <c>CreateConsumerSupportTicketRequest</c>.
/// </summary>
public sealed record CreatePurchaseReviewRequest(Guid TenantId, Guid PosTransactionId, int Rating, string? Comment);

/// <summary>PUT /api/reviews/{id}/reply.</summary>
public sealed record ReplyToPurchaseReviewRequest(string ReplyText);
