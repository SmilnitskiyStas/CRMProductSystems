using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class PosPromotionSnapshotTests
{
    internal static PosPromotionSnapshots Service(string secret = "synthetic-promotions-key-0123456789012345") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret }).Build());

    [Fact]
    public void Scope_authentication_domain_separation_and_original_versions()
    {
        var svc = Service(); var tenant = Guid.NewGuid(); var store = Guid.NewGuid(); var register = Guid.NewGuid();
        var at = DateTime.UtcNow;
        var row = new PublishedPromotionRuleRow(Guid.NewGuid(), "Original", at.AddDays(-1), Guid.NewGuid(), "{}");
        var proof = svc.Issue(tenant, store, register, "Europe/Kyiv", [row], at)!;
        var read = svc.Read(proof.Token, tenant, store, register, at)!;
        Assert.Equal(row, Assert.Single(read.Rules)); Assert.Equal("Europe/Kyiv", read.StoreTimeZone);
        Assert.Null(svc.Read(proof.Token, Guid.NewGuid(), store, register, at));
        Assert.Null(svc.Read(proof.Token, tenant, Guid.NewGuid(), register, at));
        Assert.Null(svc.Read(proof.Token, tenant, store, Guid.NewGuid(), at));
        Assert.Null(Service("another-synthetic-secret-01234567890123").Read(proof.Token, tenant, store, register, at));
        var corrupt = proof.Token.ToCharArray(); corrupt[20] = corrupt[20] == 'A' ? 'B' : 'A';
        Assert.Null(svc.Read(new string(corrupt), tenant, store, register, at));
        Assert.Null(svc.Read("p1." + proof.Token[4..], tenant, store, register, at));
        Assert.Null(svc.Read("pr1.invalid", tenant, store, register, at));
    }

    [Fact]
    public void Empty_set_is_authenticated_and_lease_uses_sale_instant()
    {
        var svc = Service(); var tenant = Guid.NewGuid(); var store = Guid.NewGuid(); var register = Guid.NewGuid();
        var at = DateTime.UtcNow.AddDays(-20);
        var proof = svc.Issue(tenant, store, register, null, [], at)!;
        Assert.Empty(svc.Read(proof.Token, tenant, store, register, at.AddDays(12))!.Rules);
        Assert.NotNull(svc.Read(proof.Token, tenant, store, register, at.AddDays(14)));
        Assert.NotNull(svc.Read(proof.Token, tenant, store, register, at.AddMinutes(-5)));
        Assert.Null(svc.Read(proof.Token, tenant, store, register, at.AddDays(14).AddTicks(1)));
        Assert.Null(svc.Read(proof.Token, tenant, store, register, at.AddMinutes(-5).AddTicks(-1)));
    }

    [Fact]
    public void Oversized_sets_are_never_silently_truncated()
    {
        var svc = Service(); var id = Guid.NewGuid(); var at = DateTime.UtcNow;
        var row = new PublishedPromotionRuleRow(id, "Rule", at, Guid.NewGuid(), "{}");
        Assert.Null(svc.Issue(id, id, id, null, Enumerable.Repeat(row, 129).ToArray(), at));
        Assert.Null(svc.Issue(id, id, id, null, [row with { DefinitionJson = new string('x', 131072) }], at));
        Assert.Null(svc.Read("pr1." + new string('a', 175000), id, id, id, at));
    }

    [Fact]
    public async Task Sync_renews_proof_on_not_modified_and_binds_the_registered_store()
    {
        var tenant = Guid.NewGuid(); var store = Guid.NewGuid(); var reg = Guid.NewGuid();
        var registers = Substitute.For<ICashRegisterRepository>();
        registers.GetByIdAsync(reg, Arg.Any<CancellationToken>()).Returns(new CashRegister { Id = reg, TenantId = tenant, StoreId = store });
        var svc = new PosSyncService(Substitute.For<ICatalogSyncRepository>(), registers: registers, promotionSnapshots: Service());
        var (first, error, _) = await svc.GetPromotionsAsync(tenant, reg, Guid.NewGuid(), null);
        Assert.Null(error); Assert.NotNull(first!.PromotionSnapshot);
        var read = Service().Read(first.PromotionSnapshot!, tenant, store, reg, first.ServerTime);
        Assert.NotNull(read); Assert.Empty(read.Rules);
        var (next, _, _) = await svc.GetPromotionsAsync(tenant, reg, null, first.Etag);
        Assert.True(next!.NotModified); Assert.Empty(next.Rules);
        Assert.NotEqual(first.PromotionSnapshot, next.PromotionSnapshot);
        Assert.NotNull(Service().Read(next.PromotionSnapshot!, tenant, store, reg, next.ServerTime));
        var (fallback, _, _) = await svc.GetPromotionsAsync(tenant, null, store, null);
        Assert.Null(fallback!.PromotionSnapshot);
        var (denied, _, status) = await svc.GetPromotionsAsync(Guid.NewGuid(), reg, store, null);
        Assert.Null(denied); Assert.Equal(403, status);
    }
}
