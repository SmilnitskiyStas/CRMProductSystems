using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Pos;
public sealed class PosScopedPriceTests
{
    internal static PosScopedPrices Proofs(string secret = "synthetic-scoped-prices-key-0123456789") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret }).Build());
    private readonly Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), register = Guid.NewGuid(), item = Guid.NewGuid();
    private readonly DateTime at = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private CatalogSyncRow Row() => new(item, "Original", ["111"], 25, 20, "kg", true, at, TenantId: tenant, PricePurchase: 9876);
    [Fact] public void Scoped_lease_is_authenticated_bound_and_half_open()
    {
        var p = Proofs(); var token = p.Issue(Row(), store, register, at, at.AddMinutes(2), null);
        Assert.StartsWith("pb1.", token.Token); Assert.DoesNotContain("9876", token.Token);
        Assert.Equal(9876, p.Read(token.Token, tenant, store, register, "111", at)!.Price.UnitCost);
        Assert.Equal("legacy_item_base", p.Read(token.Token, tenant, store, register, "111", at)!.Basis!.Source);
        Assert.Null(p.Read(token.Token, Guid.NewGuid(), store, register, "111", at));
        Assert.Null(p.Read(token.Token, tenant, Guid.NewGuid(), register, "111", at));
        Assert.Null(p.Read(token.Token, tenant, store, Guid.NewGuid(), "111", at));
        Assert.Null(p.Read(token.Token, tenant, store, register, "other", at));
        Assert.Null(p.Read(token.Token, tenant, store, register, "111", at.AddMinutes(2)));
        Assert.Null(p.Read(token.Token, tenant, store, register, "111", at.AddMinutes(-6)));
        Assert.Null(Proofs("different-scoped-prices-key-0123456789012").Read(token.Token, tenant, store, register, "111", at));
        Assert.Null(p.Read("pb1." + token.Token[4..^5] + "abcde", tenant, store, register, "111", at));
    }
    [Fact] public void Base_source_version_is_frozen_and_cursor_cannot_be_used_as_price_proof()
    {
        var p = Proofs(); var basis = new PosBasePriceDecision(item, "planned-base.v1", "planned_price_list", 25, 20,
            Guid.NewGuid(), Guid.NewGuid(), 3, new string('a', 64), at, at.AddMinutes(1), 50, 7);
        var token = p.Issue(Row(), store, register, at, basis.NextBoundary, basis);
        Assert.Equal(basis, p.Read(token.Token, tenant, store, register, "111", at.AddSeconds(10))!.Basis);
        var cursor = p.Cursor(new(tenant, store, register, "generation", item, at, at.AddMinutes(1)));
        Assert.NotNull(p.ReadCursor(cursor, tenant, register, at));
        Assert.Null(p.ReadCursor(cursor, tenant, Guid.NewGuid(), at));
        Assert.Null(p.ReadCursor(cursor, tenant, register, at.AddMinutes(1)));
        Assert.Null(p.Read(cursor, tenant, store, register, "111", at));
        Assert.Null(p.ReadCursor(token.Token, tenant, register, at));
        Assert.Throws<ArgumentException>(() => p.Issue(Row(), store, register, at, null, basis with { Price = 26 }));
    }
    [Fact] public void Frozen_comparison_uses_quote_instant_and_cannot_cross_known_boundary()
    {
        var basis = new PosBasePriceDecision(item, "planned-base.v1", "legacy_item_base", 25, 20,
            null, null, null, null, at, at.AddMinutes(1));
        var line = new BasketPricingLine(item, null, 25, 1, 20, false, []);
        Assert.True(PlannedBasePricing.Matches(basis, line, at.AddSeconds(30), true));
        Assert.False(PlannedBasePricing.Matches(basis, line, at.AddSeconds(30)));
        Assert.False(PlannedBasePricing.Matches(basis, line, at.AddMinutes(1), true));
    }
}
