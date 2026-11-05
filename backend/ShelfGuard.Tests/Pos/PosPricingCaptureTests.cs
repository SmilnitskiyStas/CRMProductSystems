using System.Text.Json;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class PosPricingCaptureTests
{
    private static readonly DateTime At = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static PosTransaction Sale() => new() { TenantId = Guid.NewGuid(), StoreId = Guid.NewGuid(),
        PricingArchiveRequired = true, TotalAmount = 10, TaxAmount = 1.67m,
        Items = [new() { ProductId = Guid.NewGuid(), Quantity = 1, PriceRetail = 10, PriceFinal = 10,
            UnitCost = 1234567, LineTotal = 10, VatRate = 20, TaxableAmount = 10, VatAmount = 1.67m }] };

    [Theory]
    [InlineData("pos-online", false, false)]
    [InlineData("pos-offline-legacy", true, false)]
    [InlineData("pos-offline-pr1", true, true)]
    public void Archive_retains_exact_result_and_honest_context_origin(string channel, bool frozenBase, bool frozenPromo)
    {
        var tx = Sale(); var capture = new PosPricingCapture(At, channel, frozenBase, frozenPromo);
        capture.Line("Original", "kg", null, new(tx.Items.First().ProductId, null, 10, 1, 20, false, []));
        var snapshot = capture.Complete(tx, [], 0, 0, null);
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(snapshot.Payload, Json)!;
        Assert.Equal(channel, d.Channel); Assert.Equal(frozenBase, d.FrozenBase); Assert.Equal(frozenPromo, d.FrozenPromotions);
        Assert.Equal("server-acceptance", d.LoyaltyContextOrigin); Assert.Equal(At, d.EvaluatedAt);
        Assert.Equal(10, d.Result.Paid); Assert.Equal(1.67m, d.Result.Vat); Assert.Equal("kg", d.Lines[0].Unit);
        Assert.Equal(PosPricingCapture.Hash(snapshot.Payload), snapshot.ContentHash);
        tx.TotalAmount = 999; Assert.Equal(10, d.Result.Paid);
    }

    [Fact]
    public void Full_capture_does_not_inherit_shadow_truncation_and_does_not_store_secrets_or_cost()
    {
        var tx = Sale(); var capture = new PosPricingCapture(At, "pos-online", false, false);
        for (var i = 0; i < 101; i++) capture.Line("Original", "pcs", null, new(Guid.NewGuid(), null, null, 1, 0, false, []));
        var snapshot = capture.Complete(tx, [], 0, 0, null);
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(snapshot.Payload, Json)!;
        Assert.Equal(101, d.Lines.Count); Assert.All(d.Lines, l => Assert.Null(l.Input.RetailPrice));
        Assert.DoesNotContain("unitCost", snapshot.Payload); Assert.DoesNotContain("barcode", snapshot.Payload);
        Assert.DoesNotContain("membershipId", snapshot.Payload); Assert.DoesNotContain("1234567", snapshot.Payload);
    }

    [Fact]
    public void Rule_source_retains_original_version_and_malformed_definition_fingerprint_without_raw_content()
    {
        var tx = Sale(); var capture = new PosPricingCapture(At, "pos-offline-pr1", true, true);
        var rule = new PublishedPromotionRuleRow(Guid.NewGuid(), "Original", At, Guid.NewGuid(),
            "{\"type\":\"percent_off\",\"params\":{\"percent\":10}}");
        const string broken = "{invalid-sensitive-raw-json";
        capture.Promotions([rule, rule with { RuleId = Guid.NewGuid(), DefinitionJson = broken }], "Europe/Kyiv");
        var snapshot = capture.Complete(tx, [], 0, 0, null);
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(snapshot.Payload, Json)!;
        Assert.Equal(rule.VersionId, d.Rules[0].VersionId); Assert.Equal("Europe/Kyiv", d.TimeZone);
        Assert.False(d.Rules[1].Valid); Assert.Equal(PosPricingCapture.Hash(broken), d.Rules[1].DefinitionHash);
        Assert.DoesNotContain(broken, snapshot.Payload);
    }

    [Fact]
    public void Loyalty_ledger_effects_and_lot_consumption_are_distinct_from_cashback()
    {
        var capture = new PosPricingCapture(At, "pos-online", false, false); var tx = Sale();
        capture.FirstPurchaseEligible = true;
        var lot = new LoyaltyBonusLot { RemainingAmount = 3, ExpiresAt = new DateTimeOffset(At.AddDays(20)) };
        capture.Consumed(lot, 2);
        var reward = new LoyaltyLedgerEntry { EntryType = LoyaltyEntryType.Reward, Amount = 5, BalanceAfter = 8 };
        capture.Effect(reward, new DateTimeOffset(At.AddDays(30)));
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(capture.Complete(tx, [], 4, 1, 8).Payload, Json)!;
        Assert.True(d.FirstPurchaseEligible); Assert.Equal(1, d.Result.AccruedBonus); Assert.Equal(8, d.BalanceAfter);
        Assert.Equal(5, d.BonusEffects.Single().Amount); Assert.Equal(reward.Id, d.BonusEffects.Single().LedgerId);
        Assert.Equal(2, d.ConsumedLots.Single().Amount); Assert.Equal(3, d.ConsumedLots.Single().RemainingAfter);
        Assert.Equal(2, d.RedemptionWithoutLots);
    }
}
