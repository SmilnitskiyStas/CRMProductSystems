namespace ShelfGuard.Application.Features.Customers;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    string[] Tags,
    int TotalOrders,
    decimal TotalSpent,
    DateTime CreatedAt
);

public sealed record CreateCustomerDto(
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    string[]? Tags
);

public sealed record UpdateCustomerDto(
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    string[]? Tags
);

public sealed record CustomerDetailDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    string[] Tags,
    int TotalOrders,
    decimal TotalSpent,
    DateTime CreatedAt,
    List<CustomerTransactionDto> RecentTransactions,
    // TASK-618: loyalty tier/progress, open-ticket count, recent reviews — one response so the
    // staff-facing customer detail view avoids N+1 round-trips. All three tier fields are null
    // together when the customer never joined the loyalty program at this tenant (no linked
    // LoyaltyMembership) — not an error. CompositeScore can be populated with TierProgressPercent
    // still null (membership exists but hasn't cleared even the lowest tier's threshold yet).
    string? CurrentTierName,
    decimal? CompositeScore,
    decimal? TierProgressPercent,
    int OpenTicketCount,
    List<CustomerReviewSummaryDto> RecentReviews
);

public sealed record CustomerTransactionDto(
    Guid Id,
    decimal TotalAmount,
    string PaymentType,
    DateTime CreatedAt,
    string Status
);

/// <summary>TASK-618: one of a customer's most recent <c>PurchaseReview</c>s, for the Customers
/// detail view. Slimmer than <c>Features.Reviews.Dtos.PurchaseReviewDto</c> — no consumer/tenant
/// identity fields, since the reviewer is already the customer this DTO is nested under.</summary>
public sealed record CustomerReviewSummaryDto(
    short Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    string? ReplyText
);
