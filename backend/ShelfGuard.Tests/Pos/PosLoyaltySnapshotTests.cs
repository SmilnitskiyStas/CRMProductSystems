using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class PosLoyaltySnapshotTests
{
    internal static PosLoyaltySnapshots Service(string secret = "synthetic-loyalty-snapshot-secret-0123456789") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret }).Build());
    internal static PosLoyaltyTerms Terms() => PosLoyaltyTerms.From(new LoyaltyMembership(), new LoyaltyProgramSettings(), true);

    [Fact]
    public void Proof_is_bound_to_tenant_store_register_member_and_key_without_balance()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(); var at = DateTime.UtcNow; var svc = Service();
        var quote = svc.Issue(ids[0], ids[1], ids[2], ids[3], ids[4], Terms(), at)!;
        var value = svc.Read(quote.Token, ids[0], ids[1], ids[2], ids[3], at)!;
        Assert.Equal(ids[4], value.CustomerId); Assert.Equal(3, value.Terms.CashbackPercent);
        for (var i = 0; i < 4; i++)
        {
            var altered = ids.ToArray(); altered[i] = Guid.NewGuid();
            Assert.Null(svc.Read(quote.Token, altered[0], altered[1], altered[2], altered[3], at));
        }
        Assert.Null(Service("another-synthetic-key-01234567890123456").Read(quote.Token, ids[0], ids[1], ids[2], ids[3], at));
        var bad = quote.Token.ToCharArray(); bad[20] = bad[20] == 'A' ? 'B' : 'A';
        Assert.Null(svc.Read(new string(bad), ids[0], ids[1], ids[2], ids[3], at));
        Assert.Null(svc.Read("pr1." + quote.Token[4..], ids[0], ids[1], ids[2], ids[3], at));
        Assert.Null(svc.Read("lo1.invalid", ids[0], ids[1], ids[2], ids[3], at));
        Assert.DoesNotContain("\"Balance\":", System.Text.Json.JsonSerializer.Serialize(value));
    }

    [Fact]
    public void Lease_is_checked_at_sale_time_and_preserves_the_entire_terms_set()
    {
        var id = Guid.NewGuid(); var at = DateTime.UtcNow.AddDays(-20); var svc = Service();
        var terms = Terms() with { TierId = Guid.NewGuid(), TierDiscountPercent = 25, CashbackPercent = 10,
            ExclusionsEnabled = true, ExcludedItems = [Guid.NewGuid()], ExcludedCategories = [Guid.NewGuid()], UnitsPerCurrency = 100 };
        var q = svc.Issue(id, id, id, id, null, terms, at)!;
        var got = svc.Read(q.Token, id, id, id, id, at.AddDays(12))!;
        Assert.Equal(25, got.Terms.TierDiscountPercent); Assert.Equal(100, got.Terms.UnitsPerCurrency);
        Assert.Equal(terms.ExcludedItems, got.Terms.ExcludedItems); Assert.Equal(terms.ExcludedCategories, got.Terms.ExcludedCategories);
        Assert.NotNull(svc.Read(q.Token, id, id, id, id, at.AddDays(14)));
        Assert.Null(svc.Read(q.Token, id, id, id, id, at.AddDays(14).AddTicks(1)));
        Assert.Null(svc.Read(q.Token, id, id, id, id, at.AddMinutes(-6)));
    }

    [Fact]
    public void Invalid_or_oversized_terms_are_never_partially_issued()
    {
        var id = Guid.NewGuid(); var svc = Service();
        foreach (var terms in new[] { Terms() with { TierDiscountPercent = 101 }, Terms() with { UnitsPerCurrency = 0 },
            Terms() with { MinimumBalance = -1 }, Terms() with { ExcludedItems = new Guid[10001] } })
            Assert.Null(svc.Issue(id, id, id, id, null, terms, DateTime.UtcNow));
        Assert.Null(svc.Read("lo1." + new string('a', 1400000), id, id, id, id, DateTime.UtcNow));
    }

    [Fact]
    public async Task Issuer_uses_authoritative_register_and_only_active_resolved_membership()
    {
        var tenant = Guid.NewGuid(); var store = Guid.NewGuid();
        var register = new CashRegister { TenantId = tenant, StoreId = store };
        typeof(CashRegister).GetProperty(nameof(CashRegister.Status))!.SetValue(register, CashRegister.StatusActive);
        var member = new LoyaltyMembership { TenantId = tenant };
        var registers = Substitute.For<ICashRegisterRepository>(); var loyalty = Substitute.For<ILoyaltyRepository>();
        registers.GetByIdAsync(register.Id, Arg.Any<CancellationToken>()).Returns(register);
        loyalty.GetMembershipByIdAsync(member.Id, tenant, Arg.Any<CancellationToken>()).Returns(member);
        loyalty.IsCheckoutProgramAvailableAsync(tenant, Arg.Any<CancellationToken>()).Returns(true);
        var issuer = new PosLoyaltyQuoteIssuer(registers, loyalty, Service(), TimeProvider.System);
        var result = await issuer.ForResolvedAsync(tenant, register.Id, member.Id, default);
        Assert.NotNull(result);
        Assert.NotNull(Service().Read(result.Token, tenant, store, register.Id, member.Id, DateTime.UtcNow));
        Assert.Null(await issuer.ForResolvedAsync(Guid.NewGuid(), register.Id, member.Id, default));
        member.Status = LoyaltyMembershipStatus.Blocked;
        Assert.Null(await issuer.ForResolvedAsync(tenant, register.Id, member.Id, default));
    }
}
