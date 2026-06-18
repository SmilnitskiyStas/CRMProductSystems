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
    List<CustomerTransactionDto> RecentTransactions
);

public sealed record CustomerTransactionDto(
    Guid Id,
    decimal TotalAmount,
    string PaymentType,
    DateTime CreatedAt,
    string Status
);
