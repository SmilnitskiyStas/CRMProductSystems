using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Pure transform behind the one-shot TASK-661 (T14) backfill of the legacy free-text
/// <c>supplier_profiles.DeliveryRegions</c> jsonb array into a structured
/// <see cref="DeliveryCoverageDto"/> (<c>supplier_profiles.DeliveryCoverage</c>).
///
/// <para>
/// I/O-free and deterministic: the console tool
/// <c>ShelfGuard.Tools.DeliveryCoverageBackfill</c> owns the DB access, the
/// <c>DeliveryCoverage IS NULL</c> idempotency guard, and the <c>provider</c> RLS override —
/// this class only decides what the new coverage should be for one profile's raw region list.
/// </para>
///
/// <para>
/// Mapping rule (per plan «eventual-whistling-rabbit», «Бекфіл / міграція даних»):
/// each free-text string is run through <see cref="UkraineRegions.TryMatchFreeText"/>;
/// a match becomes a bare <c>served</c> entry (no structured delivery fields — deduped by code,
/// first occurrence wins), and anything that does not map is collected verbatim into the
/// <c>note</c> as "<c>Також: a, b</c>" so no information from the legacy column is lost.
/// <c>notServed</c> is always empty — the legacy column only ever expressed positive coverage.
/// Match rate is expected to be low ("Вся Україна" / "по домовленості" never map).
/// </para>
/// </summary>
public static class DeliveryRegionsBackfill
{
    /// <summary>Prefix prepended to the comma-joined list of unmapped free-text regions.</summary>
    public const string UnmatchedNotePrefix = "Також: ";

    /// <param name="rawRegions">
    /// The already-parsed <c>DeliveryRegions</c> jsonb string array. Null, empty, and
    /// blank/whitespace entries are all tolerated.
    /// </param>
    /// <returns>
    /// <see cref="DeliveryRegionsBackfillResult.Coverage"/> is <c>null</c> when there is nothing
    /// worth persisting (no mappable code and no leftover free text) — the tool then leaves the
    /// row untouched. Otherwise it carries the matched region codes as bare <c>served</c> entries
    /// (no structured delivery fields), an empty <c>notServed</c>, and — when at least one string
    /// did not map — a <c>note</c>. A note-only result (empty <c>served</c>) is still returned and written, so the
    /// legacy free text stays visible rather than being silently dropped.
    /// </returns>
    public static DeliveryRegionsBackfillResult Build(IEnumerable<string>? rawRegions)
    {
        var matched = new List<string>();
        var matchedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();
        var unmatchedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawRegions ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var code = UkraineRegions.TryMatchFreeText(raw);
            if (code is not null)
            {
                if (matchedSeen.Add(code))
                    matched.Add(code);
            }
            else
            {
                var trimmed = raw.Trim();
                if (unmatchedSeen.Add(trimmed))
                    unmatched.Add(trimmed);
            }
        }

        if (matched.Count == 0 && unmatched.Count == 0)
            return new DeliveryRegionsBackfillResult(null, matched, unmatched);

        var note = unmatched.Count > 0
            ? UnmatchedNotePrefix + string.Join(", ", unmatched)
            : null;

        var coverage = new DeliveryCoverageDto(
            matched.Select(c => new DeliveryCoverageEntryDto(c, null, null, null, null)).ToList(),
            Array.Empty<string>(),
            note);

        return new DeliveryRegionsBackfillResult(coverage, matched, unmatched);
    }
}

/// <summary>
/// Outcome of <see cref="DeliveryRegionsBackfill.Build"/> for one profile.
/// <see cref="MatchedCodes"/> / <see cref="Unmatched"/> are exposed separately from
/// <see cref="Coverage"/> so the tool can log per-row detail and aggregate a run summary.
/// </summary>
public sealed record DeliveryRegionsBackfillResult(
    DeliveryCoverageDto? Coverage,
    IReadOnlyList<string> MatchedCodes,
    IReadOnlyList<string> Unmatched);
