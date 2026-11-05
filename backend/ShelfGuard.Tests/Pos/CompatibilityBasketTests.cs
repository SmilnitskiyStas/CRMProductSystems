using System.Diagnostics.Metrics;
using System.Text.Json;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Features.Promotions;
using Xunit;

namespace ShelfGuard.Tests.Pos;
public sealed class CompatibilityBasketTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Store = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Item = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset At = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static BasketPricingInput Input(decimal? price = 100, decimal quantity = 1) => new(Tenant, Store, At, "Europe/Kyiv", false,
        [new(Item, null, price, quantity, 20, false, [])], [], null);
    private static BasketLoyalty Member() => new(true, true, null, 10, 3, 100, 0, 10000, 0, 50,
        false, false, true, true, [], []);
    private static PromotionRuleInput Rule(string type, decimal amount, int? limit = null) => new(Guid.NewGuid(), Guid.NewGuid(), "Rule", At.AddDays(-1),
        new() { Type = type, Targets = new() { ItemIds = [Item] }, Params = type == "percent_off" ? new() { Percent = amount } : type == "fixed_price" ? new() { Price = amount } : new() { Amount = amount }, Limits = new() { MaxApplicationsPerReceipt = limit } });

    [Theory][InlineData("percent_off", 15, 85)][InlineData("fixed_off", 15, 85)][InlineData("fixed_price", 15, 15)]
    public void Simple_promotions_keep_rule_versions_and_totals(string type, decimal value, decimal expected)
    {
        var rule = Rule(type, value); var r = CompatibilityBasket.Resolve(Input() with { Rules = [rule] });
        Assert.Equal(expected, r.Paid); Assert.Equal(rule.VersionId, Assert.Single(r.Segments).RuleVersionId);
        Assert.Equal(100 - expected, Assert.Single(r.Applications).Discount);
    }
    [Fact] public void Missing_base_reproduces_explicit_legacy_zero_without_rewriting_catalog_policy()
    { var r = CompatibilityBasket.Resolve(Input(null)); Assert.Equal(0, r.Paid); Assert.Equal(0, r.Vat); }
    [Fact] public void Stock_plus_tier_and_weighted_promo_keep_distinct_rounding_rules()
    {
        var i = Input(10.05m, .5m); i = i with { Lines = [i.Lines[0] with { HasCriticalBatch = true, AutoDiscounts = [new(Guid.NewGuid(), 20, 8.04m)] }], Loyalty = Member() };
        Assert.Equal(7.04m, Assert.Single(CompatibilityBasket.Resolve(i).Segments).FinalPrice); // 1.005 tier -> 1.00 ToEven
        var r = CompatibilityBasket.Resolve(i with { Rules = [Rule("percent_off", 15)] });
        Assert.Equal(6.83m, Assert.Single(r.Segments).FinalPrice); Assert.Equal(3.42m, r.Paid); Assert.Equal(.57m, r.Vat);
    }
    [Fact] public void First_highest_percent_null_discount_does_not_fall_back_to_the_next_candidate()
    {
        var i = Input() with { Lines = [Input().Lines[0] with { HasCriticalBatch = true,
            AutoDiscounts = [new(Guid.NewGuid(), 50, null), new(Guid.NewGuid(), 20, 80)] }] };
        Assert.Equal(100, CompatibilityBasket.Resolve(i).Paid);
    }
    [Fact] public void Equal_percent_discounts_preserve_repository_order_and_no_critical_batch_ignores_them()
    {
        var line = Input().Lines[0] with { HasCriticalBatch = true, AutoDiscounts = [new(Guid.NewGuid(), 20, 79), new(Guid.NewGuid(), 20, 80)] };
        Assert.Equal(79, CompatibilityBasket.Resolve(Input() with { Lines = [line] }).Paid);
        Assert.Equal(100, CompatibilityBasket.Resolve(Input() with { Lines = [line with { HasCriticalBatch = false }] }).Paid);
    }
    [Fact] public void Inactive_loyalty_ignores_tier_redemption_and_cashback()
    { var r = CompatibilityBasket.Resolve(Input() with { Loyalty = Member() with { Enabled = false, RedeemBonus = 500 } }); Assert.Equal(100, r.Paid); Assert.Equal(0, r.AccruedBonus); Assert.Equal(0, r.RedeemedBonus); }
    [Fact] public void No_redemption_does_not_apply_balance_or_redemption_cap_eligibility_checks()
    {
        var r = CompatibilityBasket.Resolve(Input() with { Loyalty = Member() with {
            TierPercent = 0, Balance = -10, MinimumBalance = 100, RedeemBonus = 0 } });
        Assert.Equal(100, r.Paid); Assert.Equal(300, r.AccruedBonus);
    }
    [Fact] public void Mixed_VAT_and_redemption_exclusions_allocate_each_paid_cent()
    {
        var b = Guid.NewGuid(); var i = Input() with { Lines = [Input().Lines[0], new(b, null, 50, 1, 7, false, [])],
            Loyalty = Member() with { TierPercent = 0, RedeemBonus = 500, ExclusionsEnabled = true, ExcludedItems = [b] } };
        var r = CompatibilityBasket.Resolve(i); Assert.Equal(145, r.Paid); Assert.Equal(285, r.AccruedBonus);
        Assert.Equal(96.67m, r.Segments[0].Taxable); Assert.Equal(48.33m, r.Segments[1].Taxable); Assert.Equal(19.27m, r.Vat);
        Assert.Equal(r.Paid, r.Segments.Sum(s => s.Taxable));
    }
    [Theory][InlineData(false, 149.51)][InlineData(true, 149.50)]
    public void Exclusion_accrual_uses_raw_bonus_conversion_while_normal_accrual_uses_paid_cents(bool exclusions, decimal expected)
    {
        var r = CompatibilityBasket.Resolve(Input() with { Loyalty = Member() with { TierPercent = 0, UnitsPerCurrency = 3, RedeemBonus = 1,
            CashbackPercent = 50, ExclusionsEnabled = exclusions } });
        Assert.Equal(99.67m, r.Paid); Assert.Equal(expected, r.AccruedBonus);
    }
    [Theory][InlineData("cap")][InlineData("balance")][InlineData("minimum")]
    public void Invalid_redemption_context_is_distinguished_from_a_price_difference(string kind)
    {
        var p = Member() with { TierPercent = 0, RedeemBonus = 6000 };
        if (kind == "balance") p = p with { RedeemBonus = 500, Balance = 100 };
        if (kind == "minimum") p = p with { RedeemBonus = 500, Balance = 600, MinimumBalance = 200 };
        var i = Input() with { Loyalty = p }; Assert.Equal("redemption_not_eligible", Assert.Throws<BasketPricingException>(() => CompatibilityBasket.Resolve(i)).Code);
    }
    [Fact] public void Buy_two_get_one_is_conditional_and_keeps_partially_regular_segments()
    {
        var rule = Rule("buy_n_get_m", 0) with { Definition = new() { Type = "buy_n_get_m", Targets = new() { ItemIds = [Item] },
            Params = new() { BuyQty = 2, GetQty = 1, GetPrice = .01m, MatchMode = "per_item" } } };
        var r = CompatibilityBasket.Resolve(Input(30, 3.5m) with { Rules = [rule] }); Assert.Equal(75.01m, r.Paid);
        Assert.Equal(2, r.Segments.Count); Assert.Equal(2.5m, r.Segments[0].Quantity); Assert.Null(r.Segments[0].RuleId);
        Assert.Equal(1m, r.Segments[1].Quantity); Assert.Equal(29.99m, Assert.Single(r.Applications).Discount);
    }
    [Fact] public void Same_total_different_rule_or_VAT_is_a_mismatch_and_inputs_are_not_mutated()
    {
        var rule = Rule("percent_off", 10); var i = Input() with { Rules = [rule] }; var before = JsonSerializer.Serialize(i);
        var r = CompatibilityBasket.Resolve(i); var changed = r with { Segments = [r.Segments[0] with { RuleVersionId = Guid.NewGuid() }] };
        Assert.Contains("segments", PricingShadow.Compare(i, changed).Differences); Assert.Equal(before, JsonSerializer.Serialize(i));
        Assert.Equal("matched", PricingShadow.Compare(i, r).Outcome);
    }
    [Fact] public void Canonical_hash_ignores_decimal_scale_but_retains_context_and_frozen_offline_provenance()
    {
        Assert.Equal(CompatibilityBasket.Fingerprint(Input(1.00m, 1.00m)), CompatibilityBasket.Fingerprint(Input(1m, 1m)));
        Assert.NotEqual(CompatibilityBasket.Fingerprint(Input()), CompatibilityBasket.Fingerprint(Input() with { FrozenOfflineBase = true }));
    }
    [Fact] public void Discarded_or_invalid_published_rules_are_not_silently_counted_as_matches()
    {
        Assert.Equal("invalid", PricingShadow.Compare(Input() with { DiscardedRuleDefinitions = 1 }, null!).Outcome);
        Assert.Equal("invalid", PricingShadow.Compare(Input() with { Rules = [Rule("percent_off", 101)] }, null!).Outcome);
    }
    [Theory][InlineData("lines")][InlineData("rules")][InlineData("quantity")][InlineData("work")]
    public void Unsupported_or_unbounded_inputs_are_reported_and_not_counted_as_matches(string kind)
    {
        var i = Input(); if (kind == "lines") i = i with { Lines = Enumerable.Repeat(i.Lines[0], 101).ToArray() };
        if (kind == "rules") i = i with { Rules = Enumerable.Range(0, 501).Select(_ => Rule("percent_off", 1)).ToArray() };
        if (kind == "quantity") i = i with { Lines = [i.Lines[0] with { Quantity = .001m }] };
        if (kind == "work") i = Input(100, 2001) with { Rules = [Rule("percent_off", 10)] };
        Assert.Equal(kind == "quantity" ? "invalid" : "limit", PricingShadow.Compare(i, null!).Outcome);
    }
    [Fact] public void Metrics_expose_only_fixed_outcomes_and_default_off_never_resolves()
    {
        var tags = new System.Collections.Concurrent.ConcurrentQueue<string>(); using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "AgruSystems.Pricing.Shadow") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, values, _) => { foreach (var tag in values) tags.Enqueue(tag.Key+"="+tag.Value); }); listener.Start();
        var observer = new PricingShadow(false, new HashSet<(Guid, Guid)>()); Assert.False(observer.IsEnabled(Tenant, Store)); Assert.Equal(1, observer.Counts.Disabled);
        observer.Observe(Input(), null!); Assert.Equal(2, observer.Counts.Disabled); Assert.Equal(0, observer.Counts.Errors);
        observer = new(true, new HashSet<(Guid, Guid)> { (Tenant, Store) }); Assert.True(observer.IsEnabled(Tenant, Store)); Assert.False(observer.IsEnabled(Guid.NewGuid(), Store));
        observer.Observe(Input(), CompatibilityBasket.Resolve(Input())); observer.Observe(Input(), CompatibilityBasket.Resolve(Input()) with { Paid = 1 }); observer.ReplaySkipped();
        Assert.Equal((1L, 1L, 1L), (observer.Counts.Matched, observer.Counts.Mismatch, observer.Counts.Replays));
        Assert.All(tags.ToArray(), t => Assert.Contains(t, new[] { "outcome=disabled", "outcome=matched", "outcome=mismatch", "outcome=replay", "outcome=invalid", "outcome=limit", "outcome=error" }));
    }
}
