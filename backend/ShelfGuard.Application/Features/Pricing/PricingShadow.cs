using System.Diagnostics.Metrics;
using ShelfGuard.Application.Features.Promotions;

namespace ShelfGuard.Application.Features.Pricing;

public sealed record PricingShadowReport(string Outcome, IReadOnlyList<string> Differences, string? InputHash, string? ResultHash);
public sealed record PricingShadowCounts(long Disabled, long Matched, long Mismatch, long Invalid, long Errors, long Limits, long Replays);
public interface IPricingShadow
{
    bool IsEnabled(Guid tenant, Guid store);
    void Observe(BasketPricingInput input, BasketPricingResult actual);
    void ReplaySkipped();
}

/// <summary>Bounded synchronous CPU-only observer. Metrics carry only fixed outcome names, never receipt data.</summary>
public sealed class PricingShadow(bool enabled, IReadOnlySet<(Guid Tenant, Guid Store)> scopes) : IPricingShadow
{
    private static readonly Meter Meter = new("AgruSystems.Pricing.Shadow", "1.0.0");
    private static readonly Counter<long> Evaluations = Meter.CreateCounter<long>("agru.pricing.shadow.attempts");
    private long disabled, matched, mismatch, invalid, errors, limits, replays;
    public bool IsEnabled(Guid tenant, Guid store)
    {
        if (enabled && scopes.Contains((tenant, store))) return true;
        Interlocked.Increment(ref disabled); Count("disabled"); return false;
    }
    private static void Count(string outcome)
    {
        // A faulty diagnostics subscriber must never affect checkout.
        try { Evaluations.Add(1, new KeyValuePair<string, object?>("outcome", outcome)); } catch { }
    }
    public static PricingShadowReport Compare(BasketPricingInput input, BasketPricingResult actual)
    {
        try
        {
            var expected = CompatibilityBasket.Resolve(input); var diff = new List<string>();
            if (!expected.Segments.SequenceEqual(actual.Segments)) diff.Add("segments");
            if (!expected.Applications.OrderBy(a => a.RuleId).SequenceEqual(actual.Applications.OrderBy(a => a.RuleId))) diff.Add("applications");
            if (expected.Gross != actual.Gross) diff.Add("gross");
            if (expected.Paid != actual.Paid) diff.Add("paid");
            if (expected.Vat != actual.Vat) diff.Add("vat");
            if (expected.RedeemedBonus != actual.RedeemedBonus || expected.RedeemedCurrency != actual.RedeemedCurrency) diff.Add("redemption");
            if (expected.AccruedBonus != actual.AccruedBonus) diff.Add("accrual");
            return new(diff.Count == 0 ? "matched" : "mismatch", diff,
                CompatibilityBasket.Fingerprint(input), CompatibilityBasket.Fingerprint(expected));
        }
        catch (BasketPricingException e) { return new(e.Code == "limit" ? "limit" : "invalid", [e.Code], null, null); }
        catch { return new("error", ["resolver_error"], null, null); }
    }
    public void Observe(BasketPricingInput input, BasketPricingResult actual)
    {
        if (!IsEnabled(input.TenantId, input.StoreId)) return;
        var r = Compare(input, actual);
        switch (r.Outcome)
        {
            case "matched": Interlocked.Increment(ref matched); break;
            case "mismatch": Interlocked.Increment(ref mismatch); break;
            case "invalid": Interlocked.Increment(ref invalid); break;
            case "limit": Interlocked.Increment(ref limits); break;
            default: Interlocked.Increment(ref errors); break;
        }
        Count(r.Outcome);
    }
    public void ReplaySkipped() { Interlocked.Increment(ref replays); Count("replay"); }
    public void ConfigurationRejected() { Interlocked.Increment(ref errors); Count("error"); }
    public PricingShadowCounts Counts => new(Interlocked.Read(ref disabled), Interlocked.Read(ref matched), Interlocked.Read(ref mismatch),
        Interlocked.Read(ref invalid), Interlocked.Read(ref errors), Interlocked.Read(ref limits), Interlocked.Read(ref replays));
}

/// <summary>Never holds tracked entities, barcodes, customer/member ids, costs, or signed offline tokens.</summary>
internal sealed class PricingShadowCapture(Guid tenant, Guid store, DateTimeOffset at, bool frozen, BasketLoyalty? loyalty)
{
    private readonly List<BasketPricingLine> lines = [];
    private IReadOnlyList<PromotionRuleInput> rules = [];
    private string? timezone;
    private BasketLoyalty? capturedLoyalty = loyalty;
    private int discardedRules;
    public void Add(BasketPricingLine line) { if (lines.Count <= CompatibilityBasket.MaxLines) lines.Add(line); }
    public void Loyalty(BasketLoyalty value) => capturedLoyalty = value;
    public void Promotions(IReadOnlyList<PromotionRuleInput> values, string? zone, int discarded = 0) { rules = values; timezone = zone; discardedRules = discarded; }
    public BasketPricingInput Input => new(tenant, store, at, timezone, frozen, lines, rules, capturedLoyalty, discardedRules);
}
