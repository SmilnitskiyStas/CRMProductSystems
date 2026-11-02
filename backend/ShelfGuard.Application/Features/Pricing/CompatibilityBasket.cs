using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Pricing;

public sealed record BasketStockDiscount(Guid Id, decimal Percent, decimal? DiscountedPrice);
public sealed record BasketPricingLine(Guid ItemId, Guid? CategoryId, decimal? RetailPrice, decimal Quantity,
    decimal VatRate, bool HasCriticalBatch, IReadOnlyList<BasketStockDiscount> AutoDiscounts);
public sealed record BasketLoyalty(bool Enabled, bool HasMember, Guid? TierId, decimal TierPercent,
    decimal CashbackPercent, int UnitsPerCurrency, decimal RedeemBonus, decimal Balance,
    decimal MinimumBalance, decimal RedemptionCapPercent, bool ExclusionsEnabled,
    bool ExcludeDiscounted, bool ExclusionsForRedemption, bool ExclusionsForAccrual,
    IReadOnlyList<Guid> ExcludedItems, IReadOnlyList<Guid> ExcludedCategories);
public sealed record BasketPricingInput(Guid TenantId, Guid StoreId, DateTimeOffset At, string? TimeZone,
    bool FrozenOfflineBase, IReadOnlyList<BasketPricingLine> Lines, IReadOnlyList<PromotionRuleInput> Rules,
    BasketLoyalty? Loyalty, int DiscardedRuleDefinitions = 0);
public sealed record BasketPriceSegment(Guid ItemId, decimal Quantity, decimal RetailPrice, decimal Discount,
    decimal FinalPrice, decimal Gross, decimal VatRate, decimal Taxable, decimal Vat,
    Guid? RuleId, Guid? RuleVersionId, string? Label);
public sealed record BasketRuleApplication(Guid RuleId, Guid VersionId, decimal Units, decimal Discount);
public sealed record BasketPricingResult(IReadOnlyList<BasketPriceSegment> Segments,
    IReadOnlyList<BasketRuleApplication> Applications, decimal Gross, decimal RedeemedBonus,
    decimal RedeemedCurrency, decimal Paid, decimal Vat, decimal AccruedBonus);

public sealed class BasketPricingException(string code) : Exception(code) { public string Code { get; } = code; }

/// <summary>
/// A side-effect-free adapter of the existing server checkout policy, not a new price policy.
/// No catalogue, balance, stock, fiscal or historical receipt writes. P1 planned lists are not inputs.
/// </summary>
public static class CompatibilityBasket
{
    public const string Policy = "pos-compatibility.v1";
    public const int MaxLines = 100;
    public const int MaxRules = 500;
    public const int MaxExpandedUnits = 2000;
    public const int MaxRuleUnitWork = 200000;
    public static decimal Money(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    public static BasketPricingResult Resolve(BasketPricingInput input)
    {
        Validate(input);
        var loyalty = input.Loyalty;
        var enabled = loyalty is { Enabled: true, HasMember: true };
        var tier = enabled ? loyalty!.TierPercent : 0m;
        var stockDiscounts = input.Lines.Select(l => l.HasCriticalBatch
            ? l.AutoDiscounts.OrderByDescending(d => d.Percent).FirstOrDefault()?.DiscountedPrice is decimal discounted
                ? Math.Max(0, (l.RetailPrice ?? 0) - discounted) : 0m
            : 0m).ToArray();
        var lines = input.Lines.Select((l, i) => new PromotionLineInput(l.ItemId, l.CategoryId,
            Math.Max(0, (l.RetailPrice ?? 0) - stockDiscounts[i]), l.Quantity,
            stockDiscounts[i] > 0, l.RetailPrice ?? 0)).ToArray();
        var promo = PromotionEngine.Evaluate(lines, input.Rules, new(input.StoreId, input.At, 0,
            enabled ? new(Guid.Empty, loyalty!.TierId, tier) : null, input.TimeZone));
        var segments = new List<BasketPriceSegment>();
        var applications = new Dictionary<Guid, (decimal Units, decimal Discount)>();
        var rules = input.Rules.ToDictionary(r => r.Id);
        var eligibleGross = 0m;
        for (var i = 0; i < lines.Length; i++)
        {
            var l = input.Lines[i]; var retail = l.RetailPrice ?? 0;
            // The legacy writer only consumes engine segments when a RULE priced some units.
            // Tier-only engine segments must not replace its legacy stock+tier fallback.
            if (promo.Lines[i].Segments.Any(s => s.RuleId.HasValue))
            {
                foreach (var s in promo.Lines[i].Segments)
                {
                    Add(l, s.Quantity, retail - s.UnitPrice, s.UnitPrice, s.RuleId, s.Label);
                    if (s.RuleId is Guid id)
                    {
                        var prior = applications.GetValueOrDefault(id);
                        applications[id] = (prior.Units + s.Quantity,
                            prior.Discount + (lines[i].UnitPrice - s.UnitPrice) * s.Quantity);
                    }
                }
            }
            else
            {
                var discount = stockDiscounts[i];
                if (tier > 0) discount = Math.Min(retail, discount + decimal.Round(retail * tier / 100m, 2, MidpointRounding.ToEven));
                Add(l, l.Quantity, discount, Math.Max(0, retail - discount), null, null);
            }
        }
        void Add(BasketPricingLine l, decimal qty, decimal discount, decimal price, Guid? rule, string? label)
        {
            var gross = Money(price * qty);
            segments.Add(new(l.ItemId, qty, l.RetailPrice ?? 0, discount, price, gross, l.VatRate, 0, 0,
                rule, rule is Guid id ? rules[id].VersionId : null, label));
            var excluded = enabled && loyalty!.ExclusionsEnabled &&
                (loyalty.ExcludedItems.Contains(l.ItemId) || l.CategoryId is Guid cat && loyalty.ExcludedCategories.Contains(cat)
                 || loyalty.ExcludeDiscounted && discount > 0);
            if (!excluded) eligibleGross += gross;
        }
        var total = segments.Sum(s => s.Gross); var redeemed = 0m; var currency = 0m; var accrued = 0m;
        if (enabled)
        {
            redeemed = Math.Max(0, loyalty!.RedeemBonus);
            currency = Money(redeemed / loyalty.UnitsPerCurrency);
            if (redeemed > 0)
            {
                var capBase = loyalty.ExclusionsEnabled && loyalty.ExclusionsForRedemption ? eligibleGross : total;
                var cap = decimal.Floor(decimal.Round(capBase * loyalty.RedemptionCapPercent / 100m, 2, MidpointRounding.ToEven) * loyalty.UnitsPerCurrency * 100m) / 100m;
                if (redeemed > cap || redeemed > loyalty.Balance || loyalty.Balance - redeemed < loyalty.MinimumBalance)
                    throw new BasketPricingException("redemption_not_eligible");
            }
            var accrualBase = loyalty.ExclusionsEnabled && loyalty.ExclusionsForAccrual
                ? Math.Max(0, eligibleGross - redeemed / loyalty.UnitsPerCurrency) : total - currency;
            accrued = Money(accrualBase * loyalty.CashbackPercent / 100m * loyalty.UnitsPerCurrency);
            if (accrued < 0) accrued = 0; // legacy does not book nonpositive accrual
        }
        var paid = total - currency;
        var taxRows = segments.Select(s => new PosTransactionItem { ProductId = s.ItemId,
            Quantity = s.Quantity, PriceFinal = s.FinalPrice, LineTotal = s.Gross, VatRate = s.VatRate }).ToList();
        var vat = PosTaxCalculator.Apply(taxRows, paid);
        segments = segments.Select((s, i) => s with { Taxable = taxRows[i].TaxableAmount!.Value, Vat = taxRows[i].VatAmount!.Value }).ToList();
        return new(segments, applications.OrderBy(a => a.Key).Select(a => new BasketRuleApplication(a.Key,
            rules[a.Key].VersionId, a.Value.Units, Money(a.Value.Discount))).ToArray(), total, redeemed, currency, paid, vat, accrued);
    }

    private static void Validate(BasketPricingInput input)
    {
        if (input.Lines.Count is < 1 or > MaxLines || input.Rules.Count > MaxRules) throw new BasketPricingException("limit");
        if (input.DiscardedRuleDefinitions > 0 || input.Rules.Any(r => !PromotionEngine.IsValid(r.Definition)))
            throw new BasketPricingException("invalid_rules");
        if (input.Rules.Any(r => r.Definition.Targets.ItemIds.Count + r.Definition.Targets.CategoryIds.Count > 1000
            || r.Definition.Scope.StoreIds.Count > 1000 || r.Definition.Audience.TierIds.Count > 1000)) throw new BasketPricingException("limit");
        if (input.Lines.Any(l => l.Quantity <= 0 || l.Quantity > 99_999_999.99m || decimal.Round(l.Quantity, 2) != l.Quantity
            || l.RetailPrice < 0 || l.VatRate is < 0 or > 100 || l.AutoDiscounts.Count > 1000)) throw new BasketPricingException("invalid_line");
        var whole = input.Lines.Sum(l => decimal.Floor(l.Quantity));
        // Above the engine's own unit threshold it takes its cheap tier-only fallback.
        // Below it, cap the *additional* shadow work more tightly than the authoritative engine.
        if (input.Rules.Count > 0 && whole <= PromotionEngine.MaxUnitsPerReceipt &&
            (whole + input.Lines.Count > MaxExpandedUnits || (whole + input.Lines.Count) * input.Rules.Count > MaxRuleUnitWork))
            throw new BasketPricingException("limit");
        if (input.Rules.DistinctBy(r => r.Id).Count() != input.Rules.Count) throw new BasketPricingException("duplicate_rule");
        if (input.Loyalty is { Enabled: true, HasMember: true } p && (p.UnitsPerCurrency <= 0
            || p.TierPercent is < 0 or > 100 || p.ExcludedItems.Count + p.ExcludedCategories.Count > 10000)) throw new BasketPricingException("invalid_loyalty");
    }

    private sealed class ExactDecimal : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }
    private static readonly JsonSerializerOptions Canonical = new(JsonSerializerDefaults.Web) { Converters = { new ExactDecimal() } };
    public static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Canonical))).ToLowerInvariant();
}
