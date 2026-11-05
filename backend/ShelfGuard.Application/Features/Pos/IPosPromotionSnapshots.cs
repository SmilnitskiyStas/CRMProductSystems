using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Pos;

/// <summary>The entire server-issued promotion set, including the absence of rules.</summary>
public sealed record PosPromotionSnapshot(Guid TenantId, Guid StoreId, Guid RegisterId,
    string? StoreTimeZone, IReadOnlyList<PublishedPromotionRuleRow> Rules,
    DateTime IssuedAt, DateTime ExpiresAt);

public sealed record IssuedPosPromotionSnapshot(string Token, DateTime ExpiresAt);

public interface IPosPromotionSnapshots
{
    // Null means this set exceeds the offline protocol bounds. Never issue a partial set.
    IssuedPosPromotionSnapshot? Issue(Guid tenantId, Guid storeId, Guid registerId,
        string? timeZone, IReadOnlyList<PublishedPromotionRuleRow> rules, DateTime issuedAt);
    PosPromotionSnapshot? Read(string token, Guid tenantId, Guid storeId, Guid registerId, DateTime soldAt);
}
