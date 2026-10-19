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

// ── Customer activity / analytics page ────────────────────────────────────────────────────────

/// <summary>Everything the customer page needs to analyse one customer's purchase behaviour.</summary>
public sealed record CustomerActivityDto(
    CustomerActivityStatsDto Stats,
    List<CustomerStoreActivityDto> Stores,
    List<CustomerMonthActivityDto> Months,
    List<CustomerTopProductDto> TopProducts);

public sealed record CustomerActivityStatsDto(
    int ReceiptCount,
    decimal TotalSpent,
    decimal AverageCheck,
    decimal ItemsQuantity,
    DateTime? FirstPurchaseAt,
    DateTime? LastPurchaseAt,
    int? DaysSinceLastPurchase,
    int DaysSinceRegistered,
    /// <summary>Average days between two consecutive purchases (first→last / (receipts-1)); null when under 2 receipts.</summary>
    decimal? AverageDaysBetweenPurchases,
    int Receipts30d,
    decimal Spent30d,
    int Receipts90d,
    decimal Spent90d);

public sealed record CustomerStoreActivityDto(
    Guid StoreId, string StoreName, int Receipts, decimal Spent, decimal SharePercent, DateTime FirstAt, DateTime LastAt);

public sealed record CustomerMonthActivityDto(string Month, int Receipts, decimal Spent);

public sealed record CustomerTopProductDto(
    Guid ProductId, string Name, decimal Quantity, decimal Spent, int Receipts, DateTime LastAt);

public sealed record CustomerReceiptLineDto(
    Guid ProductId, string Name, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total);

public sealed record CustomerReceiptDto(
    Guid Id,
    string ReceiptNumber,
    string? FiscalNumber,
    DateTime CreatedAt,
    Guid StoreId,
    string StoreName,
    decimal Total,
    string PaymentType,
    string Status,
    List<CustomerReceiptLineDto> Lines);
