namespace ShelfGuard.Application.Features.Orders.Dtos;

/// <summary>One step of an order's life, for the timeline (At is null when it has not happened yet).</summary>
public sealed record OrderTimelineStepDto(string Key, DateTime? At, string? By);

public sealed record TrackedOrderLineDto(
    string Name, decimal Quantity, decimal? QuantityReceived, string? Unit, decimal? Amount);

/// <summary>
/// A purchase order as the buyer sees it. <c>Source</c> = "receipt" | "marketplace";
/// <c>Status</c> = new | confirmed | in_transit | delivered | cancelled.
/// </summary>
public sealed record TrackedOrderDto(
    string Source,
    Guid Id,
    string? Number,
    string Status,
    string? Counterparty,
    Guid? StoreId,
    string? StoreName,
    DateTime CreatedAt,
    DateOnly? ExpectedAt,
    bool IsOverdue,
    int? DaysOverdue,
    decimal TotalQuantity,
    decimal TotalAmount,
    string? Note,
    List<OrderTimelineStepDto> Timeline,
    List<TrackedOrderLineDto> Lines,
    int PostponeCount,
    DateOnly? OriginalExpectedAt,
    DateTime? PostponedAt,
    /// <summary>True for open internal orders — the only ones the buyer can postpone (a marketplace date is set by the supplier).</summary>
    bool CanPostpone);

public sealed record OrderTrackingSummaryDto(
    int New, int Confirmed, int InTransit, int Delivered, int Cancelled, int Overdue, int Open,
    decimal OpenQuantity, decimal InTransitQuantity, decimal OverdueQuantity, int Postponed);

public sealed record OrderTrackingPageDto(
    List<TrackedOrderDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);
