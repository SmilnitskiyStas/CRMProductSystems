using System.Text.Json;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Pos;

/// <summary>Sale terms only. Balance, blocked/left status and prior reward claims are always checked live.</summary>
public sealed record PosLoyaltyTerms(bool Enabled, Guid? TierId, decimal TierDiscountPercent, decimal CashbackPercent,
    int UnitsPerCurrency, decimal RedemptionCapPercent, decimal MinimumBalance,
    bool ExclusionsEnabled, bool ExcludeDiscounted, bool ExclusionsForRedemption, bool ExclusionsForAccrual,
    Guid[] ExcludedItems, Guid[] ExcludedCategories, bool FirstRewardEnabled, decimal FirstReward,
    bool LifetimeEnabled, int LifetimeDays, bool DefaultsUsed, Guid? SettingsId, DateTimeOffset? SettingsUpdatedAt)
{
    public static PosLoyaltyTerms From(LoyaltyMembership m, LoyaltyProgramSettings s, bool defaults) => new(s.IsEnabled,
        m.CurrentTierId, m.CurrentTier?.DiscountPercent ?? 0, m.CurrentTier?.AccrualMultiplier ?? s.AccrualRatePercent,
        s.BonusUnitsPerCurrencyUnit, s.RedemptionCapPercent, s.MinRedemptionBalance, s.BonusExclusionsEnabled,
        s.ExcludeDiscountedItems, s.ExclusionsApplyToRedemption, s.ExclusionsApplyToAccrual,
        s.BonusExclusionsEnabled ? JsonSerializer.Deserialize<Guid[]>(s.ExcludedProductIdsJson) ?? [] : [],
        s.BonusExclusionsEnabled ? JsonSerializer.Deserialize<Guid[]>(s.ExcludedCategoryIdsJson) ?? [] : [],
        s.FirstPurchaseRewardEnabled, s.FirstPurchaseRewardAmount, s.BonusLifetimeEnabled, s.BonusLifetimeDays,
        defaults, defaults ? null : s.Id, defaults ? null : s.UpdatedAt);
    public bool Valid => TierDiscountPercent is >= 0 and <= 100 && CashbackPercent is >= 0 and <= 100
        && UnitsPerCurrency is >= 1 and <= 1000000 && RedemptionCapPercent is >= 0 and <= 100
        && MinimumBalance >= 0 && FirstReward >= 0 && (!LifetimeEnabled || LifetimeDays is >= 1 and <= 36500)
        && ExcludedItems is { Length: <= 10000 } && ExcludedCategories is { Length: <= 10000 };
    public LoyaltyProgramSettings Settings(Guid tenant) => new() { TenantId = tenant, IsEnabled = Enabled,
        AccrualRatePercent = CashbackPercent, BonusUnitsPerCurrencyUnit = UnitsPerCurrency,
        RedemptionCapPercent = RedemptionCapPercent, MinRedemptionBalance = MinimumBalance,
        BonusExclusionsEnabled = ExclusionsEnabled, ExcludeDiscountedItems = ExcludeDiscounted,
        ExclusionsApplyToRedemption = ExclusionsForRedemption, ExclusionsApplyToAccrual = ExclusionsForAccrual,
        ExcludedProductIdsJson = JsonSerializer.Serialize(ExcludedItems), ExcludedCategoryIdsJson = JsonSerializer.Serialize(ExcludedCategories),
        FirstPurchaseRewardEnabled = FirstRewardEnabled, FirstPurchaseRewardAmount = FirstReward,
        BonusLifetimeEnabled = LifetimeEnabled, BonusLifetimeDays = LifetimeDays };
}
public sealed record PosLoyaltySnapshot(Guid TenantId, Guid StoreId, Guid RegisterId, Guid MembershipId, Guid? CustomerId,
    PosLoyaltyTerms Terms, DateTime IssuedAt, DateTime ExpiresAt);
public sealed record IssuedPosLoyaltySnapshot(string Token, DateTime ExpiresAt);
public sealed record PosLoyaltyGrant(string Token, DateTime ExpiresAt, Guid? TierId, string? TierName,
    decimal DiscountPercent, bool Enabled, Guid? CustomerId);
public interface IPosLoyaltySnapshots
{
    IssuedPosLoyaltySnapshot? Issue(Guid tenant, Guid store, Guid register, Guid member, Guid? customer, PosLoyaltyTerms terms, DateTime at);
    PosLoyaltySnapshot? Read(string token, Guid tenant, Guid store, Guid register, Guid member, DateTime soldAt);
}
public interface IPosLoyaltyQuoteIssuer
{
    // Only called after existing code/identifier authentication has resolved this membership.
    Task<PosLoyaltyGrant?> ForResolvedAsync(Guid tenant, Guid register, Guid member, CancellationToken ct);
}
