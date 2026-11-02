using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Pricing;

public sealed record PricingLineSource(string Name, string Unit, string? ProofHash, BasketPricingLine Input,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PosBasePriceDecision? BaseResolution = null);
public sealed record PricingRuleSource(Guid RuleId, Guid VersionId, string Name, DateTime CreatedAt,
    string DefinitionHash, PromotionDefinition? Definition, bool Valid);
public sealed record PricingLoyaltySource(bool DefaultsUsed, Guid? SettingsId, DateTimeOffset? SettingsUpdatedAt,
    bool FirstPurchaseRewardEnabled, decimal FirstPurchaseRewardAmount, bool BonusLifetimeEnabled, int BonusLifetimeDays,
    decimal ConfiguredTierDiscount, decimal? BalanceBefore, string? TermsProofHash = null,
    DateTime? TermsIssuedAt = null, DateTime? TermsExpiresAt = null);
public sealed record PricingBonusConsumption(Guid LotId, decimal Amount, decimal RemainingAfter, DateTimeOffset? ExpiresAt);
public sealed record PricingBonusEffect(Guid LedgerId, string Kind, decimal Amount, decimal BalanceAfter, DateTimeOffset? ExpiresAt);
public sealed record PricingArchivedDecision(int Version, string Policy, string Channel, DateTime EvaluatedAt,
    bool FrozenBase, bool FrozenPromotions, string LoyaltyContextOrigin, string? TimeZone,
    IReadOnlyList<PricingLineSource> Lines, IReadOnlyList<PricingRuleSource> Rules, BasketLoyalty? Loyalty,
    PricingLoyaltySource? LoyaltySource, bool? FirstPurchaseEligible, decimal? BalanceAfter,
    IReadOnlyList<PricingBonusEffect> BonusEffects, IReadOnlyList<PricingBonusConsumption> ConsumedLots,
    decimal RedemptionWithoutLots, BasketPricingResult Result);

/// <summary>Detached pricing evidence. Never holds cost, barcodes, proof tokens, customer/member identities or tracked entities.</summary>
public sealed class PosPricingCapture(DateTime at, string channel, bool frozenBase, bool frozenPromotions)
{
    public const string Protocol = "pos.pricing.archive.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<PricingLineSource> lines = [];
    private PricingRuleSource[] rules = [];
    private string? zone;
    private BasketLoyalty? loyalty;
    private PricingLoyaltySource? source;
    private readonly List<PricingBonusEffect> effects = [];
    private readonly List<PricingBonusConsumption> consumed = [];
    public void Consumed(LoyaltyBonusLot lot, decimal amount) => consumed.Add(new(lot.Id, amount, lot.RemainingAmount, lot.ExpiresAt));
    public string LoyaltyContextOrigin { get; set; } = "server-acceptance";
    public bool? FirstPurchaseEligible { get; set; }
    public void Line(string name, string unit, string? proofHash, BasketPricingLine input, PosBasePriceDecision? basis = null)
        => lines.Add(new(name, unit, proofHash, input, basis));
    public void Promotions(IReadOnlyList<PublishedPromotionRuleRow> published, string? timeZone)
    {
        zone = timeZone;
        rules = published.Select(r => {
            var definition = PromotionDefinitionJson.TryParse(r.DefinitionJson);
            return new PricingRuleSource(r.RuleId, r.VersionId, r.Name, r.CreatedAt, Hash(r.DefinitionJson),
                definition, PromotionEngine.IsValid(definition));
        }).ToArray();
    }
    public void Loyalty(BasketLoyalty? input, PricingLoyaltySource? context) { loyalty = input; source = context; }
    public void Effect(LoyaltyLedgerEntry entry, DateTimeOffset? expiry = null) => effects.Add(new(entry.Id,
        entry.EntryType.ToString(), entry.Amount, entry.BalanceAfter, expiry));
    public PosPricingSnapshot Complete(PosTransaction tx, IReadOnlyList<PromotionRuleApplication> applications,
        decimal redeemed, decimal accrued, decimal? balanceAfter)
    {
        var versions = applications.ToDictionary(a => a.RuleId, a => a.VersionId);
        var segments = tx.Items.Select(i => new BasketPriceSegment(i.ProductId, i.Quantity, i.PriceRetail,
            i.DiscountAmount, i.PriceFinal, i.LineTotal!.Value, i.VatRate!.Value, i.TaxableAmount!.Value,
            i.VatAmount!.Value, i.PromotionRuleId, i.PromotionRuleId is Guid id ? versions[id] : null, i.PromotionLabel)).ToArray();
        var gross = segments.Sum(i => i.Gross);
        var result = new BasketPricingResult(segments, applications.Select(a => new BasketRuleApplication(a.RuleId,
            a.VersionId, a.Units, a.DiscountAmount)).ToArray(), gross, redeemed, gross - tx.TotalAmount,
            tx.TotalAmount, tx.TaxAmount, accrued);
        var payload = JsonSerializer.Serialize(new PricingArchivedDecision(1, CompatibilityBasket.Policy, channel,
            at, frozenBase, frozenPromotions, LoyaltyContextOrigin, zone, lines, rules, loyalty, source,
            FirstPurchaseEligible, balanceAfter, effects, consumed, redeemed - consumed.Sum(c => c.Amount), result), Json);
        return new() { TenantId = tx.TenantId, StoreId = tx.StoreId, TransactionId = tx.Id, Channel = channel,
            EvaluatedAt = at, Payload = payload, ContentHash = Hash(payload) };
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
