using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Promotions;

/// <summary>Real-Postgres check of ItemRepository.SearchByBarcodeFragmentAsync (soft-skips without Postgres).</summary>
public sealed class ItemBarcodeFragmentSearchIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _cs = string.Empty;
    private bool _ready;
    private Guid _tenantA, _tenantB;
    private Guid _milk, _upc, _foreign;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    public ItemBarcodeFragmentSearchIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cs = TestPostgres.ResolveConnectionString();
        try
        {
            await using var probe = new NpgsqlConnection(_cs);
            await probe.OpenAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Skipping - no Postgres: {ex.Message}");
            return;
        }
        _ready = true;

        await using var db = NewContext();
        var a = Tenant.Create($"Frag A {_run}", $"frag-a-{_run}");
        var b = Tenant.Create($"Frag B {_run}", $"frag-b-{_run}");
        _tenantA = a.Id;
        _tenantB = b.Id;
        var milk = new Item { TenantId = a.Id, Name = "Frag Milk", Barcodes = ["4823117500123"], PriceRetail = 10m };
        var upc = new Item { TenantId = a.Id, Name = "Frag Upc", Barcodes = ["0012345678905", "abc-777"], PriceRetail = 10m };
        var inactive = new Item { TenantId = a.Id, Name = "Frag Old", Barcodes = ["4823117599999"], IsActive = false };
        var foreign = new Item { TenantId = b.Id, Name = "Frag Foreign", Barcodes = ["4823117500456"] };
        _milk = milk.Id;
        _upc = upc.Id;
        _foreign = foreign.Id;
        db.Tenants.AddRange(a, b);
        db.Items.AddRange(milk, upc, inactive, foreign);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_ready) return;
        await using var db = NewContext();
#pragma warning disable EF1002
        foreach (var t in new[] { _tenantA, _tenantB })
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM items WHERE \"TenantId\" = {0}", t);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM tenants WHERE \"Id\" = {0}", t);
        }
#pragma warning restore EF1002
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_cs);

    private async Task<List<Guid>> Search(Guid tenant, string digits)
    {
        await using var db = NewContext();
        return (await new ItemRepository(db).SearchByBarcodeFragmentAsync(tenant, digits, 50)).Select(i => i.Id).ToList();
    }

    [Fact]
    public async Task Fragments_and_leading_zero_variants_match_within_the_tenant_only()
    {
        if (!_ready) { _output.WriteLine("skipped"); return; }

        Assert.Equal([_milk], await Search(_tenantA, "4823117500123")); // full
        Assert.Equal([_milk], await Search(_tenantA, "48231175"));      // prefix
        Assert.Equal([_milk], await Search(_tenantA, "500123"));        // suffix
        Assert.Equal([_milk], await Search(_tenantA, "3117500"));       // substring
        Assert.Equal([_upc], await Search(_tenantA, "12345678905"));    // zeros stripped on the stored side
        Assert.Equal([_upc], await Search(_tenantA, "0000012345678905")); // extra zeros on the query side
        Assert.Equal([_upc], await Search(_tenantA, "345678"));         // substring of the zero-padded code
        Assert.Empty(await Search(_tenantA, "00000"));                  // all zeros: never a strip-equality match
        Assert.Empty(await Search(_tenantA, "99999"));                  // inactive item excluded
        Assert.Equal([_foreign], await Search(_tenantB, "500456"));     // tenant isolation (explicit filter)
        Assert.Empty(await Search(_tenantA, "500456"));
        Assert.Empty(await Search(_tenantA, "48x23"));                  // non-digit query is rejected
        Assert.Empty(await Search(_tenantA, "1' OR '1'='1"));
    }
}
