using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class PosPriceSnapshotTests
{
    private static PosPriceSnapshots Service(string key = "synthetic-price-snapshot-secret-0123456789") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = key }).Build());

    private static CatalogSyncRow Row() => new(Guid.NewGuid(), "Original", ["barcode"], 100m, 7m, "pcs", true,
        DateTime.UtcNow, TenantId: Guid.NewGuid(), PricePurchase: 45m);

    [Fact]
    public void Authenticated_snapshot_retains_values_and_rejects_tampering_tenant_barcode_and_wrong_key()
    {
        var service = Service(); var row = Row(); var at = DateTime.UtcNow;
        var token = service.Issue(row, at).Token;
        var original = service.Read(token, row.TenantId, "barcode", at)!;
        Assert.Equal(100m, original.Price); Assert.Equal(7m, original.VatRate); Assert.Equal(45m, original.UnitCost);
        var tampered = token.ToCharArray(); tampered[10] = tampered[10] == 'A' ? 'B' : 'A';
        Assert.Null(service.Read(new string(tampered), row.TenantId, "barcode", at));
        Assert.Null(service.Read(token, Guid.NewGuid(), "barcode", at));
        Assert.Null(service.Read(token, row.TenantId, "another", at));
        Assert.Null(Service("another-synthetic-secret-0123456789012345").Read(token, row.TenantId, "barcode", at));
        Assert.Null(service.Read("p1.invalid", row.TenantId, "barcode", at));
    }

    [Fact]
    public void Lease_is_checked_at_sale_time_so_a_valid_offline_sale_can_arrive_later()
    {
        var service = Service(); var row = Row(); var issued = DateTime.UtcNow.AddDays(-20);
        var quote = service.Issue(row, issued);
        Assert.NotNull(service.Read(quote.Token, row.TenantId, "barcode", issued.AddDays(12)));
        Assert.Null(service.Read(quote.Token, row.TenantId, "barcode", issued.AddDays(15)));
        Assert.Null(service.Read(quote.Token, row.TenantId, "barcode", issued.AddMinutes(-6)));
    }

    [Fact]
    public async Task Catalog_sync_exposes_an_opaque_proof_without_disclosing_purchase_cost()
    {
        var row = Row(); var repo = Substitute.For<ICatalogSyncRepository>();
        repo.GetChangedSinceAsync(null, null, 500, Arg.Any<CancellationToken>()).Returns(new[] { row });
        var result = await new PosSyncService(repo, priceSnapshots: Service()).GetCatalogAsync(null, null, null);
        var dto = Assert.Single(result.Items);
        Assert.NotNull(dto.PriceSnapshot); Assert.NotNull(dto.PriceSnapshotExpiresAt);
        Assert.Equal(row.Id, Service().Read(dto.PriceSnapshot!, row.TenantId, "barcode", result.ServerTime)!.ItemId);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("\"PricePurchase\":", json); Assert.DoesNotContain("\"UnitCost\":", json);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 6.54)]
    [InlineData(20, 16.67)]
    public void Inclusive_VAT_uses_the_line_rate(int rate, double expected)
    {
        var line = new PosTransactionItem { PriceFinal = 100m, Quantity = 1, VatRate = rate };
        Assert.Equal((decimal)expected, PosTaxCalculator.Apply([line], 100m));
        Assert.Equal(100m, line.TaxableAmount);
    }

    [Fact]
    public void Redemption_allocation_preserves_every_cent_for_mixed_VAT_and_tiny_payments()
    {
        List<PosTransactionItem> lines = [new() { PriceFinal = 100, Quantity = 1, VatRate = 0 },
            new() { PriceFinal = 100, Quantity = 1, VatRate = 7 }, new() { PriceFinal = 100, Quantity = 1, VatRate = 20 }];
        PosTaxCalculator.Apply(lines, 299.99m);
        Assert.Equal(299.99m, lines.Sum(i => i.TaxableAmount));
        Assert.Equal(new decimal?[] { 100, 100, 99.99m }, lines.Select(i => i.TaxableAmount));
        PosTaxCalculator.Apply(lines, 0.01m);
        Assert.Equal(0.01m, lines.Sum(i => i.TaxableAmount));
        Assert.All(lines, i => Assert.True(i.TaxableAmount >= 0));
    }
}
