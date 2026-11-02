using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Domain.Entities.Erp;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class PosBasePricingTests
{
    private static readonly DateTime At = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid tenant = Guid.NewGuid(), location = Guid.NewGuid(), item = Guid.NewGuid();
    private BasePriceCandidate Choice(Guid? store = null, int priority = 100, DateTime? from = null, DateTime? until = null, bool active = true, Guid? key = null) =>
        new(new() { Id = Guid.NewGuid(), TenantId = tenant, ListKey = key ?? Guid.NewGuid(), Version = 1,
            LocationId = store, Priority = priority, IsActive = active, ValidFrom = from ?? At.AddDays(-1), ValidUntil = until },
            new() { TenantId = tenant, ItemId = item, Price = 25 });
    private static BasePriceCandidate Linked(BasePriceCandidate c) => c with { Line = new() {
        TenantId = c.Line.TenantId, PriceListId = c.List.Id, ItemId = c.Line.ItemId, Price = c.Line.Price } };
    private PlannedBaseSelection Select(params BasePriceCandidate[] choices) => PlannedBasePricing.Select(tenant, location, item, At, choices.Select(Linked).ToArray());

    [Fact] public void Store_precedes_global_then_lower_priority_then_stable_list_key()
    {
        var global = Choice(priority: 0); var store = Choice(location, 10);
        var first = Choice(location, 5, key: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var other = Choice(location, 5, key: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        Assert.Equal(first.List.Id, Select(other, store, global, first).Selected!.List.Id);
        Assert.Equal(first.List.Id, Select(first, global, store, other).Selected!.List.Id);
    }
    [Fact] public void Half_open_boundaries_work_without_writes_and_report_next_transition()
    {
        var ended = Choice(location, 0, until: At); var starts = Choice(location, 1, from: At, until: At.AddSeconds(1));
        var future = Choice(location, 0, from: At.AddSeconds(2));
        var result = Select(ended, starts, future); Assert.Equal(starts.List.Id, result.Selected!.List.Id);
        Assert.Equal(At.AddSeconds(1), result.NextBoundary);
        var later = PlannedBasePricing.Select(tenant, location, item, At.AddSeconds(2), new[] { ended, starts, future }.Select(Linked).ToArray());
        Assert.Equal(future.List.Id, later.Selected!.List.Id);
    }
    [Fact] public void Foreign_store_tenant_item_and_inactive_sources_are_excluded()
    {
        var foreign = Linked(Choice());
        foreign = foreign with { Line = new() { TenantId = Guid.NewGuid(), ItemId = item, PriceListId = foreign.List.Id } };
        Assert.Null(PlannedBasePricing.Select(tenant, location, item, At, [foreign]).Selected);
        Assert.Null(Select(Choice(Guid.NewGuid()), Choice(active: false)).Selected);
        Assert.Null(PlannedBasePricing.Select(Guid.NewGuid(), location, item, At, [Linked(Choice())]).Selected);
        Assert.Null(PlannedBasePricing.Select(tenant, location, Guid.NewGuid(), At, [Linked(Choice())]).Selected);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DI_gate_requires_opt_in_and_exact_scope(bool enabled)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Pricing:PosAuthority:OnlineEnabled"] = enabled.ToString(),
            ["Pricing:PosAuthority:Scopes:0:TenantId"] = tenant.ToString(),
            ["Pricing:PosAuthority:Scopes:0:LocationId"] = location.ToString() }).Build();
        var services = new ServiceCollection(); ShelfGuard.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        using var provider = services.BuildServiceProvider(); var gate = provider.GetRequiredService<IPosOnlinePricingGate>();
        Assert.Equal(enabled, gate.Allows(tenant, location)); Assert.False(gate.Allows(tenant, Guid.NewGuid()));
        Assert.False(gate.Allows(Guid.NewGuid(), location));
    }
    [Theory] [InlineData("empty")] [InlineData("uuid")] [InlineData("bool")] [InlineData("zero")]
    public void Invalid_authority_config_fails_at_startup_without_wildcard_fallback(string kind)
    {
        var data = new Dictionary<string, string?> { ["Pricing:PosAuthority:OnlineEnabled"] = kind == "bool" ? "bad" : "true" };
        if (kind is "uuid" or "zero") { data["Pricing:PosAuthority:Scopes:0:TenantId"] = kind == "uuid" ? "bad" : Guid.Empty.ToString(); data["Pricing:PosAuthority:Scopes:0:LocationId"] = location.ToString(); }
        Assert.ThrowsAny<Exception>(() => ShelfGuard.Infrastructure.DependencyInjection.AddInfrastructure(new ServiceCollection(), new ConfigurationBuilder().AddInMemoryCollection(data).Build()));
    }
    [Fact] public void Empty_disabled_allowlist_is_off_and_scope_count_is_bounded()
    {
        Assert.False(new PosOnlinePricingGate(false, []).Allows(tenant, location));
        Assert.Throws<ArgumentException>(() => new PosOnlinePricingGate(true, Enumerable.Range(0, 101).Select(_ => new InventoryPostingScope(tenant, Guid.NewGuid()))));
    }
    [Theory] [InlineData("price")] [InlineData("vat")] [InlineData("version")]
    [InlineData("policy")] [InlineData("source")] [InlineData("item")] [InlineData("boundary")]
    public void Source_metadata_mismatch_is_detected_even_with_unchanged_final_money(string changed)
    {
        var input = new BasketPricingLine(item, null, 25, 1, 20, false, []);
        var original = new PosBasePriceDecision(item, PricingRules.Policy, "planned_price_list", 25, 20,
            Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), At, At.AddHours(1), 10, 20);
        Assert.True(PlannedBasePricing.Matches(original, input, At));
        var altered = changed switch {
            "price" => original with { Price = 24 }, "vat" => original with { VatRate = 7 },
            "version" => original with { Version = 0 }, "policy" => original with { Policy = "unknown" },
            "source" => original with { Source = "legacy_item_base" }, "item" => original with { ItemId = Guid.NewGuid() },
            _ => original with { NextBoundary = At }
        };
        Assert.False(PlannedBasePricing.Matches(altered, input, At));
    }
}
