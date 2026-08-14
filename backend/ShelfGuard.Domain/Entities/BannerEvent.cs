namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Append-only view/click log for a <see cref="Banner"/> (TASK-520) — the first view/click
/// tracking mechanism in this codebase. No update/delete methods: analytics are computed on
/// read via COUNT(...) GROUP BY EventType (GET /api/banners/{id}/analytics, TASK-521) — there
/// is no denormalized counter column to keep consistent here, unlike
/// LoyaltyMembership.Balance/LoyaltyLedgerEntry.
/// </summary>
public sealed class BannerEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BannerId { get; private set; }

    /// <summary>view | click — see <see cref="BannerEventType"/>.</summary>
    public string EventType { get; private set; } = BannerEventType.View;

    /// <summary>
    /// Null for anonymous consumer sessions — banners are public marketing content, visible
    /// and trackable without a logged-in ConsumerAccount (same [AllowAnonymous] posture as
    /// MarketplaceController's public listings).
    /// </summary>
    public Guid? ConsumerAccountId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private BannerEvent() { }

    public static BannerEvent Create(Guid tenantId, Guid bannerId, string eventType, Guid? consumerAccountId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        BannerId = bannerId,
        EventType = eventType,
        ConsumerAccountId = consumerAccountId,
        OccurredAt = DateTime.UtcNow,
    };
}

public static class BannerEventType
{
    public const string View = "view";
    public const string Click = "click";
}
