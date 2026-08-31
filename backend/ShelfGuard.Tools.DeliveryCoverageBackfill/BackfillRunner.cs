using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Tools.DeliveryCoverageBackfill;

/// <summary>
/// TASK-661 (T14): migrates every <c>supplier_profiles</c> row that still carries the legacy
/// free-text <c>DeliveryRegions</c> jsonb array and has no structured <c>DeliveryCoverage</c>
/// yet. Free-text region names that map (via <see cref="DeliveryRegionsBackfill"/> →
/// <c>UkraineRegions.TryMatchFreeText</c>) become <c>served</c> region codes; anything that does
/// not map is preserved verbatim in the coverage <c>note</c> ("<c>Також: …</c>"). The legacy
/// <c>DeliveryRegions</c> column is left in place as the audit trail — a later migration drops it.
///
/// <para>
/// RLS: a console app has no <c>HttpContext</c>, so <c>TenantConnectionInterceptor</c> RESETs
/// every RLS session variable on connection open and <c>supplier_profiles</c>
/// (FORCE ROW LEVEL SECURITY; <c>tenant_isolation</c> + <c>provider_bypass</c> +
/// <c>worker_bypass</c>, no RESTRICTIVE <c>store_scope</c>) would return zero rows. The run
/// asserts the same cross-tenant read+write bypass <c>ProviderRlsOverride</c> uses for
/// <c>MarketplaceRepository</c> — <c>SET LOCAL app.role = 'provider'</c> inside one explicit
/// transaction, which Postgres reverts automatically on commit / rollback / unhandled
/// exception. It is issued directly here rather than through the DI'd <c>IProviderRlsOverride</c>
/// because that service's security contract (and <c>ProviderRlsOverrideContainmentTests</c>)
/// restrict it to <c>MarketplaceRepository</c> only.
/// </para>
///
/// <para>Idempotent via the <c>DeliveryCoverage IS NULL</c> filter. Dry run by default.</para>
/// </summary>
public sealed class BackfillRunner
{
    private readonly AppDbContext _db;

    public BackfillRunner(AppDbContext db) => _db = db;

    public async Task<int> RunAsync(bool apply, CancellationToken ct)
    {
        Console.WriteLine("=== TASK-661 (T14) supplier_profiles.DeliveryRegions -> DeliveryCoverage backfill ===");
        Console.WriteLine(apply
            ? "MODE: APPLY — changes will be committed."
            : "MODE: DRY RUN — no writes (pass --apply to persist).");
        Console.WriteLine();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Fixed literal — no interpolation, no injection surface, no EF1002 suppression needed
        // (identical to ProviderRlsOverride). SET LOCAL reverts when this transaction ends.
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'provider'", ct);

        List<Domain.Entities.SupplierProfile> profiles;
#pragma warning disable CS0618 // DeliveryRegions is [Obsolete]; reading it is this tool's entire job.
        profiles = await _db.SupplierProfiles
            .Where(p => p.DeliveryCoverage == null && p.DeliveryRegions != null)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
#pragma warning restore CS0618

        Console.WriteLine($"Rows with DeliveryCoverage IS NULL and DeliveryRegions IS NOT NULL: {profiles.Count}");
        Console.WriteLine();

        var scanned = 0;
        var updated = 0;
        var skippedNothingToMap = 0;
        var allUnmatched = new List<string>();

        foreach (var profile in profiles)
        {
            scanned++;

#pragma warning disable CS0618 // DeliveryRegions is [Obsolete]; reading it is this tool's entire job.
            var rawJson = profile.DeliveryRegions;
#pragma warning restore CS0618
            var rawRegions = ParseJsonStringArray(rawJson);

            var result = DeliveryRegionsBackfill.Build(rawRegions);
            allUnmatched.AddRange(result.Unmatched);

            if (result.Coverage is null)
            {
                skippedNothingToMap++;
                Console.WriteLine($"  [skip]   {profile.Id}  DeliveryRegions={Describe(rawRegions, rawJson)} -> nothing to map");
                continue;
            }

            var coverageJson = DeliveryCoverageJson.Serialize(result.Coverage);
            profile.DeliveryCoverage = coverageJson;
            updated++;

            Console.WriteLine(
                $"  [update] {profile.Id}  matched=[{string.Join(", ", result.MatchedCodes)}]  " +
                $"unmatched=[{string.Join(", ", result.Unmatched)}]");
            Console.WriteLine($"           {coverageJson}");
        }

        if (apply)
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            await tx.RollbackAsync(ct);
        }

        var distinctUnmatched = allUnmatched
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine($"Rows scanned:                 {scanned}");
        Console.WriteLine($"Rows updated:                  {updated}  ({(apply ? "committed" : "rolled back — dry run")})");
        Console.WriteLine($"Rows skipped (nothing to map): {skippedNothingToMap}");
        Console.WriteLine($"Total unmatched values:        {allUnmatched.Count}  ({distinctUnmatched.Count} distinct)");
        if (distinctUnmatched.Count > 0)
        {
            Console.WriteLine("Distinct unmatched strings (kept in each row's coverage note):");
            foreach (var s in distinctUnmatched)
                Console.WriteLine($"  - {s}");
        }
        Console.WriteLine();
        Console.WriteLine(apply ? "Status: OK (applied)" : "Status: OK (dry run — re-run with --apply to persist)");

        return 0;
    }

    /// <summary>
    /// Parses the <c>DeliveryRegions</c> jsonb string-array value. Mirrors
    /// <c>MarketplaceService.DeserializeStringArray</c>. Returns <c>null</c> for
    /// null / blank / non-array JSON.
    /// </summary>
    private static IReadOnlyList<string>? ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(IReadOnlyList<string>? parsed, string? rawJson) =>
        parsed is null ? $"(unparseable: {rawJson})" : $"[{string.Join(", ", parsed)}]";
}
