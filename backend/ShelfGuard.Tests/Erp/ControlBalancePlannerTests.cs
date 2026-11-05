using ShelfGuard.Application.Features.Erp.Backfill;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class ControlBalancePlannerTests
{
    [Fact]
    public void Maintenance_sales_bound_is_explicit_bounded_and_part_of_request_identity()
    {
        var s = Snapshot(); var fingerprint = new InventoryDryRunAnalyzer().Analyze(s).SourceFingerprint;
        var request = new ControlBalanceRequest(s.TenantId, s.LocationId, Guid.NewGuid(), fingerprint,
            [s.Batches[0].Id], "Reviewed control balance", "copy acceptance", MaxRows: 10);
        var ordinary = ControlBalancePlanner.RequestHash(request);
        Assert.NotEqual(ordinary, ControlBalancePlanner.RequestHash(request with { MaxSaleRows = 300_000 }));
        Assert.NotEqual(ControlBalancePlanner.RequestHash(request with { MaxSaleRows = 300_000 }),
            ControlBalancePlanner.RequestHash(request with { MaxSaleRows = 400_000 }));
        Assert.Throws<ArgumentException>(() => ControlBalancePlanner.Validate(request with { MaxSaleRows = 9 }));
        Assert.Throws<ArgumentException>(() => ControlBalancePlanner.Validate(request with { MaxSaleRows = 500_001 }));
    }

    private static InventorySnapshot Snapshot()
    {
        var tenant = Guid.NewGuid(); var location = Guid.NewGuid(); var item = Guid.NewGuid();
        var at = DateTime.UtcNow;
        return new(tenant, location, at, "test-only", [new(item, tenant, "Current captured name", "кг")], [],
            [new(Guid.NewGuid(), tenant, item, location, null, 3.25m, 5, at.AddDays(-3), null, null, "lot", new(2026, 12, 1))],
            [], [], [], [], [], []);
    }
    private static ControlBalanceRequest Request(InventorySnapshot s) => new(s.TenantId, s.LocationId, Guid.NewGuid(),
        new InventoryDryRunAnalyzer().Analyze(s).SourceFingerprint, s.Batches.Select(x => x.Id).ToArray(), "Reviewed control opening", "synthetic-acceptance");

    [Fact]
    public void Plan_uses_captured_quantity_unit_and_existing_identity_protocol_without_inventing_history()
    {
        var s = Snapshot(); var request = Request(s); var plan = Assert.Single(ControlBalancePlanner.Plan(request, s));
        Assert.Equal(3.25m, plan.Batch.Quantity); Assert.Equal("кг", plan.Item.Unit);
        Assert.Equal("кг", Assert.Single(new InventoryDryRunAnalyzer().Analyze(s).Batches).CurrentUnit);
        Assert.Equal(InventoryDryRunAnalyzer.Identity(s.TenantId, "legacy.control_balance", plan.Batch.Id), plan.Identity);
        Assert.Empty(s.Movements);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("quantity")]
    [InlineData("scope")]
    public void Changed_sources_or_scope_reject_the_whole_plan(string change)
    {
        var s = Snapshot(); var request = Request(s);
        s = change switch { "unit" => s with { Items = [s.Items[0] with { Unit = "л" }] },
            "quantity" => s with { Batches = [s.Batches[0] with { Quantity = 4 }] },
            _ => s with { LocationId = Guid.NewGuid() } };
        Assert.Throws<MaterializationConflictException>(() => ControlBalancePlanner.Plan(request, s));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("reason")]
    [InlineData("acceptance")]
    [InlineData("run")]
    [InlineData("hash")]
    public void Selection_and_audit_parameters_are_explicit(string invalid)
    {
        var s = Snapshot(); var r = Request(s);
        r = invalid switch { "empty" => r with { BatchIds = [] }, "duplicate" => r with { BatchIds = [r.BatchIds[0], r.BatchIds[0]] },
            "reason" => r with { Reason = " " }, "acceptance" => r with { AcceptanceReference = "" },
            "run" => r with { RunId = Guid.Empty }, _ => r with { SourceFingerprint = new string('z', 64) } };
        Assert.Throws<ArgumentException>(() => ControlBalancePlanner.Validate(r));
    }

    [Fact]
    public void Request_hash_is_order_independent_but_binds_selection_scope_run_reason_and_acceptance()
    {
        var s = Snapshot(); var r = Request(s) with { BatchIds = [Guid.NewGuid(), Guid.NewGuid()] };
        var hash = ControlBalancePlanner.RequestHash(r);
        Assert.Equal(hash, ControlBalancePlanner.RequestHash(r with { BatchIds = r.BatchIds.Reverse().ToArray(), MaxRows = 100 }));
        foreach (var changed in new[] { r with { RunId = Guid.NewGuid() }, r with { LocationId = Guid.NewGuid() },
            r with { BatchIds = [Guid.NewGuid()] }, r with { Reason = "Other" }, r with { AcceptanceReference = "Other" } })
            Assert.NotEqual(hash, ControlBalancePlanner.RequestHash(changed));
    }
}
