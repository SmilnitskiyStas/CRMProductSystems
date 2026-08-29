using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-478 fix — live-Postgres coverage for <see cref="PostCampaignRepository.FindCustomersByIdsOrPhonesAsync"/>,
/// the exact gap the QA writeup flagged (bug-task476-phone-import-matching-format-mismatch):
/// every existing PostCampaign test mocks <see cref="IPostCampaignRepository"/> entirely, so
/// nothing ever exercised the real EF/SQL translation against a realistically-varied stored
/// <see cref="Customer.Phone"/> value — which is exactly where the original bug lived (raw
/// string-equality against whatever format happened to be stored, silently missing every format
/// except the exact canonical one). Same connection/skip pattern as
/// <c>AudienceBuilderRepositoryIntegrationTests</c>: real <c>crm</c> superuser connection, no RLS
/// session vars (SQL correctness under test, not tenant isolation).
/// </summary>
public sealed class PostCampaignRepositoryIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;

    // Same real customer, five different stored phone formats (mirrors the live repro tenant
    // "Свіжий Кут" in the bug writeup: 13/14 real customers had non-canonical stored phones).
    private Guid _custCanonical;      // "+380501110001" — already worked before the fix (control)
    private Guid _custNoPlus;         // "380501110002" — 12 digits, no leading '+'
    private Guid _custLocalZero;      // "0501110003"   — 10-digit local (trunk 0) form
    private Guid _custSpacedDashed;   // "380-50-111-00-04" — dashed, matches the QA repro's own example
    private Guid _custBareNine;       // "501110005"    — bare 9-digit subscriber number

    private Guid _custNoPhone;        // Phone = null -> must never match anything, never throw
    private Guid _custGarbagePhone;   // Phone = "not-a-phone" -> unparseable, must never match
    private Guid _custIdAndPhoneBoth; // matched via BOTH its Id and its (non-canonical) phone at once

    public PostCampaignRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping PostCampaignRepository integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"PostCampaign Repo Test {Guid.NewGuid():N}", $"post-campaign-repo-test-{Guid.NewGuid():N}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        Guid NewCustomer(string name, string? phone)
        {
            var c = new Customer { TenantId = _tenantId, Name = name, Phone = phone };
            db.Customers.Add(c);
            return c.Id;
        }

        _custCanonical = NewCustomer("Клієнт Канонічний", "+380501110001");
        _custNoPlus = NewCustomer("Клієнт Без Плюса", "380501110002");
        _custLocalZero = NewCustomer("Клієнт Локальний", "0501110003");
        _custSpacedDashed = NewCustomer("Клієнт З Дефісами", "380-50-111-00-04");
        _custBareNine = NewCustomer("Клієнт Дев'ять Цифр", "501110005");
        _custNoPhone = NewCustomer("Клієнт Без Телефону", null);
        _custGarbagePhone = NewCustomer("Клієнт Сміттєвий Телефон", "not-a-phone");
        _custIdAndPhoneBoth = NewCustomer("Клієнт І Id І Телефон", "0671110008");

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task FindCustomersByIdsOrPhonesAsync_matches_every_customer_regardless_of_stored_phone_format()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PostCampaignRepository(db);

        // Exactly what SegmentImportParser.Classify hands the repository in production: every
        // candidate already normalized to +380XXXXXXXXX, regardless of how a marketer actually
        // typed/pasted it. None of these literally match any of the RAW stored strings above.
        var candidatePhones = new[]
        {
            PhoneNormalizer.Normalize("380501110002")!,
            PhoneNormalizer.Normalize("0501110003")!,
            PhoneNormalizer.Normalize("380-50-111-00-04")!,
            PhoneNormalizer.Normalize("501110005")!,
            PhoneNormalizer.Normalize("+380501110001")!, // already-canonical control case
        };

        var result = await repo.FindCustomersByIdsOrPhonesAsync(_tenantId, [], candidatePhones);

        var matchedIds = result.Select(r => r.Id).ToHashSet();
        Assert.Equal(5, result.Count);
        Assert.Contains(_custCanonical, matchedIds);
        Assert.Contains(_custNoPlus, matchedIds);
        Assert.Contains(_custLocalZero, matchedIds);
        Assert.Contains(_custSpacedDashed, matchedIds);
        Assert.Contains(_custBareNine, matchedIds);

        // Every returned Phone is the canonical normalized form, regardless of how it was stored
        // — PostCampaignService.ImportAsync's own byPhone dictionary depends on this.
        Assert.All(result, r => Assert.StartsWith("+380", r.Phone));

        // Null / unparseable stored phones must never produce a false match, never throw.
        Assert.DoesNotContain(_custNoPhone, matchedIds);
        Assert.DoesNotContain(_custGarbagePhone, matchedIds);
    }

    [Fact]
    public async Task FindCustomersByIdsOrPhonesAsync_returns_one_row_when_the_same_customer_is_matched_by_both_id_and_phone()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PostCampaignRepository(db);

        // The marketer pasted BOTH this customer's GUID and their (non-canonical-format) phone
        // in the same import — must collapse to exactly one MatchedCustomerRow, never throw a
        // duplicate-key exception when PostCampaignService.ImportAsync does
        // `found.ToDictionary(c => c.Id)`.
        var candidatePhones = new[] { PhoneNormalizer.Normalize("0671110008")! };

        var result = await repo.FindCustomersByIdsOrPhonesAsync(_tenantId, [_custIdAndPhoneBoth], candidatePhones);

        var onlyRow = Assert.Single(result, r => r.Id == _custIdAndPhoneBoth);
        Assert.Equal("+380671110008", onlyRow.Phone);
    }

    [Fact]
    public async Task FindCustomersByIdsOrPhonesAsync_is_unaffected_by_phone_format_when_no_phone_candidates_are_supplied()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new PostCampaignRepository(db);

        // Empty candidatePhones -> the phone pass must be skipped entirely; GUID-only matching
        // stays exactly as it was before this fix (no regression for GUID-only imports).
        var result = await repo.FindCustomersByIdsOrPhonesAsync(_tenantId, [_custCanonical, _custNoPhone], []);

        var matchedIds = result.Select(r => r.Id).ToHashSet();
        Assert.Equal(2, result.Count);
        Assert.Contains(_custCanonical, matchedIds);
        Assert.Contains(_custNoPhone, matchedIds);
    }

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
