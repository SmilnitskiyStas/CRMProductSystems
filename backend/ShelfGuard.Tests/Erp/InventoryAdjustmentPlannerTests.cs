using System.Globalization;
using NSubstitute;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class InventoryAdjustmentPlannerTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static InventoryAdjustmentRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        3.25m, 4.50m, "Контрольний перерахунок");
    private static InventoryAdjustmentPlan GoldenPlan() => InventoryAdjustmentPlanner.Prepare(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"), 3.25m, 4.50m, "Physical count"));

    [Theory]
    [InlineData("tenant")]
    [InlineData("actor")]
    [InlineData("request")]
    [InlineData("location")]
    [InlineData("batch")]
    public void Empty_identities_are_rejected(string field)
    {
        var r = Request();
        r = field switch { "request" => r with { RequestId = Guid.Empty }, "location" => r with { LocationId = Guid.Empty },
            "batch" => r with { BatchId = Guid.Empty }, _ => r };
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(
            field == "tenant" ? Guid.Empty : Tenant, field == "actor" ? Guid.Empty : Actor, r));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("100000000")]
    [InlineData("79228162514264337593543950335")]
    public void Both_quantities_must_fit_the_legacy_column_exactly(string value)
    {
        var quantity = decimal.Parse(value, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(Tenant, Actor, Request() with { Quantity = quantity }));
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(Tenant, Actor, Request() with { ExpectedQuantity = quantity }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Reason\nfor adjustment")]
    [InlineData("Reason\tfor adjustment")]
    public void Reason_is_required_and_must_be_printable(string reason) =>
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(Tenant, Actor, Request() with { Reason = reason }));

    [Fact]
    public void No_op_and_oversized_reason_are_rejected()
    {
        var r = Request();
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(Tenant, Actor, r with { Quantity = r.ExpectedQuantity }));
        Assert.Throws<ArgumentException>(() => InventoryAdjustmentPlanner.Prepare(Tenant, Actor, r with { Reason = new string('a', 501) }));
    }

    [Fact]
    public void Request_hash_is_stable_for_decimal_scale_reason_whitespace_and_process_culture()
    {
        Assert.Equal("6040724dc0dddbd8043270d598c2044dba0f6a2eaf0c2b1dbf6ae98cd97e8020", GoldenPlan().RequestHash);
        var r = Request(); var original = InventoryAdjustmentPlanner.Prepare(Tenant, Actor, r);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            var repeated = InventoryAdjustmentPlanner.Prepare(Tenant, Actor,
                r with { Quantity = 4.5000m, ExpectedQuantity = 3.250m, Reason = "  " + r.Reason + "  " });
            Assert.Equal(original, repeated);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void Every_scope_identity_precondition_and_business_input_is_bound_to_the_hash()
    {
        var r = Request(); var original = InventoryAdjustmentPlanner.Prepare(Tenant, Actor, r);
        var variants = new[] { r with { RequestId = Guid.NewGuid() }, r with { LocationId = Guid.NewGuid() },
            r with { BatchId = Guid.NewGuid() }, r with { Quantity = 7m }, r with { ExpectedQuantity = 2m }, r with { Reason = "Інша причина" } };
        Assert.All(variants, changed => Assert.NotEqual(original.RequestHash,
            InventoryAdjustmentPlanner.Prepare(Tenant, Actor, changed).RequestHash));
        Assert.NotEqual(original.RequestHash, InventoryAdjustmentPlanner.Prepare(Guid.NewGuid(), Actor, r).RequestHash);
        Assert.NotEqual(original.RequestHash, InventoryAdjustmentPlanner.Prepare(Tenant, Guid.NewGuid(), r).RequestHash);
    }

    [Fact]
    public void Content_hash_includes_the_captured_catalog_and_batch_metadata()
    {
        var plan = GoldenPlan();
        var s = new InventoryAdjustmentSnapshot(Guid.Parse("66666666-6666-6666-6666-666666666666"), "kg", "Flour", "BATCH-1",
            new(2027, 1, 1), Guid.Parse("77777777-7777-7777-7777-777777777777"), new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc));
        var hash = InventoryAdjustmentPlanner.ContentHash(plan, s);
        Assert.Equal("b0290bb10c3531e99984b9f7015d00d858f8fce4e89e0147ab9a19ec0bc4957a", hash);
        var variants = new[] { s with { ItemId = Guid.NewGuid() }, s with { Unit = "шт" }, s with { ItemName = "Цукор" },
            s with { BatchNumber = "BATCH-2" }, s with { ExpiryDate = s.ExpiryDate.AddDays(1) },
            s with { ZoneId = null }, s with { OccurredAt = s.OccurredAt.AddSeconds(1) } };
        Assert.All(variants, changed => Assert.NotEqual(hash, InventoryAdjustmentPlanner.ContentHash(plan, changed)));
    }

    [Fact]
    public void Gate_is_disabled_by_default_and_has_no_wildcard_scope()
    {
        var location = Guid.NewGuid(); var scopes = new[] { new InventoryPostingScope(Tenant, location) };
        Assert.False(new InventoryPostingGate(false, scopes).Allows(Tenant, location));
        var gate = new InventoryPostingGate(true, scopes);
        scopes[0] = new(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(gate.Allows(Tenant, location));
        Assert.False(gate.Allows(Guid.NewGuid(), location));
        Assert.False(gate.Allows(Tenant, Guid.NewGuid()));
        Assert.False(gate.Allows(Guid.Empty, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new InventoryPostingGate(true, []));
        Assert.Throws<ArgumentException>(() => new InventoryPostingGate(true, [new(Tenant, Guid.Empty)]));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("cashier")]
    [InlineData("storekeeper")]
    [InlineData("provider")]
    [InlineData("provider_admin")]
    [InlineData("consumer")]
    public async Task Adjustment_does_not_expand_the_existing_manager_role_floor(string role)
    {
        var r = Request(); var access = Substitute.For<IWorkAccessContext>(); var store = Substitute.For<IInventoryAdjustmentStore>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(new WorkAccessScope(Tenant, Actor, role, true, [],
            new HashSet<string> { "production.complete" }, new Dictionary<string, bool>()));
        var sut = new InventoryAdjustmentPostingService(access, new InventoryPostingGate(true, [new(Tenant, r.LocationId)]), store);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => sut.AdjustAsync(r));
        await store.DidNotReceiveWithAnyArgs().PostAsync(default!);
    }

    [Fact]
    public async Task Disabled_or_unassigned_or_unauthenticated_calls_never_reach_the_store()
    {
        var r = Request(); var access = Substitute.For<IWorkAccessContext>(); var store = Substitute.For<IInventoryAdjustmentStore>();
        var scope = new WorkAccessScope(Tenant, Actor, "store_manager", false, [r.LocationId], new HashSet<string>(), new Dictionary<string, bool>());
        var sut = new InventoryAdjustmentPostingService(access, new InventoryPostingGate(false, []), store);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => sut.AdjustAsync(r));
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(scope with { LocationIds = [] });
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => sut.AdjustAsync(r));
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(scope);
        await Assert.ThrowsAsync<InventoryPostingDisabledException>(() => sut.AdjustAsync(r));
        await store.DidNotReceiveWithAnyArgs().PostAsync(default!);
    }

    [Theory]
    [InlineData("store_manager")]
    [InlineData("network_manager")]
    [InlineData("enterprise_admin")]
    public async Task Authorized_call_uses_live_scope_instead_of_a_body_supplied_tenant_or_actor(string role)
    {
        var r = Request(); var access = Substitute.For<IWorkAccessContext>(); var store = Substitute.For<IInventoryAdjustmentStore>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(new WorkAccessScope(Tenant, Actor, role, false, [r.LocationId],
            new HashSet<string>(), new Dictionary<string, bool>()));
        var sut = new InventoryAdjustmentPostingService(access, new InventoryPostingGate(true, [new(Tenant, r.LocationId)]), store);
        await sut.AdjustAsync(r);
        await store.Received(1).PostAsync(InventoryAdjustmentPlanner.Prepare(Tenant, Actor, r), Arg.Any<CancellationToken>());
    }
}
