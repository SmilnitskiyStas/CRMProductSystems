namespace ShelfGuard.Application.Features.Analytics.Dtos;

public sealed record PosAnalyticsSummaryDto(
    decimal TotalRevenue,
    int TransactionCount,
    decimal AverageTicket,
    decimal CashRevenue,
    decimal CardRevenue,
    int ShiftCount,
    DateOnly From,
    DateOnly To);

public sealed record PosRevenueTrendDto(
    IReadOnlyList<RevenueTrendPointDto> Points,
    string GroupBy);

public sealed record RevenueTrendPointDto(
    DateOnly Date,
    decimal Revenue,
    int Transactions);

public sealed record PosTopProductsDto(
    IReadOnlyList<TopProductDto> Items);

public sealed record TopProductDto(
    Guid ProductId,
    string ProductName,
    string Barcode,
    decimal TotalRevenue,
    decimal TotalQuantity,
    int TransactionCount);

public sealed record PosCashierStatsDto(
    IReadOnlyList<CashierStatDto> Cashiers);

public sealed record CashierStatDto(
    Guid CashierId,
    string CashierName,
    decimal TotalRevenue,
    int TransactionCount,
    decimal AverageTicket,
    int ShiftCount);
