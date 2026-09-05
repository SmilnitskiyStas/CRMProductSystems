namespace ShelfGuard.Application.Features.Marketplace.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// TASK-695 (Phase 8) — a buyer rating one supplier-side employee. Two entry
// points: after a delivered order (rates the responsible manager) and from a
// chat thread (rates a staff member who replied). Supplier-internal only —
// never on the public profile, never in SupplierMetrics.Rating.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Buyer rates the manager responsible for a delivered order. Rating 1–5.</summary>
public record RateSupplierEmployeeDto(int Rating, string? Comment = null);

/// <summary>
/// Buyer rates a supplier staff member who replied in the shared chat thread. Rating 1–5.
/// <paramref name="SupplierUserId"/> must be someone who actually sent ≥1 message in the thread
/// from the supplier side — the only validation that this is a real supplier actor.
/// </summary>
public record RateChatParticipantDto(Guid SupplierUserId, int Rating, string? Comment = null);

/// <summary>
/// One buyer→supplier-employee rating, as the BUYER sees it back (to render "you already rated
/// ★★★★"). No <c>RatedByName</c> — the buyer knows who they are.
/// </summary>
public record SupplierEmployeeReviewDto(
    Guid Id,
    Guid SupplierUserId,
    string SupplierUserName,
    short Rating,
    string? Comment,
    /// <summary><c>"order"</c> or <c>"chat"</c>.</summary>
    string Source,
    Guid? OrderId,
    Guid? ChatSessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One rating as the SUPPLIER's team manager sees it (the "read the feedback" cabinet view) —
/// adds <c>RatedByName</c> so the manager knows which buyer contact left it.
/// </summary>
public record SupplierEmployeeReviewDetailDto(
    Guid Id,
    Guid SupplierUserId,
    string SupplierUserName,
    short Rating,
    string? Comment,
    string Source,
    Guid? OrderId,
    Guid? ChatSessionId,
    string? RatedByName,
    DateTimeOffset CreatedAt);
