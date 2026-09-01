using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// (De)serialization + validation for the <c>supplier_profiles.DeliveryCoverage</c> JSONB string
/// (TASK-650, restructured in TASK-665). Canonical stored shape is camelCase:
/// <code>
/// { "served": [{ "regionCode": "UA-32", "deliveryDaysMin": 1, "deliveryDaysMax": 3,
///                "minOrderAmount": 5000, "note": "Новою Поштою" }],
///   "notServed": ["UA-43"],
///   "note": "Загальна примітка" }
/// </code>
/// Same casing convention the rest of the codebase uses for JSONB string columns that are
/// (de)serialized in the application layer.
///
/// <para>
/// TASK-665 replaced the single per-region <c>terms</c> string with structured fields
/// (<c>deliveryDaysMin</c>/<c>deliveryDaysMax</c>/<c>minOrderAmount</c>/<c>note</c>). Legacy
/// dev-DB rows written in the old shape self-heal on read: a non-empty <c>terms</c> with no
/// <c>note</c> is mapped into <c>note</c>. <c>terms</c> is never written back.
/// </para>
/// </summary>
public static class DeliveryCoverageJson
{
    private const int MaxDeliveryDays = 365;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Deserializes the stored JSONB string into a DTO. Returns <c>null</c> for null/blank/invalid
    /// JSON. Tolerates missing keys (absent <c>served</c>/<c>notServed</c> become empty lists).
    /// The result is normalized (trimmed, blanks dropped, deduped, day ranges ordered) but NOT
    /// validated — callers that accept coverage from the wire must run <see cref="Validate"/> first.
    /// </summary>
    public static DeliveryCoverageDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize<Raw>(json, Options);
            if (raw is null)
                return null;

            var served = (raw.Served ?? new List<RawEntry>())
                .Where(e => e is not null)
                .Select(ToEntry)
                .ToList();

            return Normalize(new DeliveryCoverageDto(
                served,
                raw.NotServed ?? new List<string>(),
                raw.Note));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes a DTO to the canonical camelCase JSON string (normalized first).</summary>
    public static string Serialize(DeliveryCoverageDto dto)
    {
        var n = Normalize(dto);
        return JsonSerializer.Serialize(new Raw
        {
            Served = n.Served
                .Select(e => new RawEntry
                {
                    RegionCode      = e.RegionCode,
                    DeliveryDaysMin = e.DeliveryDaysMin,
                    DeliveryDaysMax = e.DeliveryDaysMax,
                    MinOrderAmount  = e.MinOrderAmount,
                    Note            = e.Note,
                    // Terms is deliberately never written — read-only back-compat only.
                })
                .ToList(),
            NotServed = n.NotServed.ToList(),
            Note = n.Note,
        }, Options);
    }

    /// <summary>
    /// Returns an error message per problem (empty = valid). After normalization: every
    /// <c>served[].regionCode</c> and every <c>notServed</c> entry must pass
    /// <see cref="UkraineRegions.IsValid"/>, no code may appear in both lists, and each served
    /// entry's structured fields must be in range.
    /// </summary>
    public static List<string> Validate(DeliveryCoverageDto dto)
    {
        var errors = new List<string>();
        if (dto is null)
            return errors;

        var n = Normalize(dto);
        var served = n.Served.Select(e => e.RegionCode).ToList();
        var notServed = n.NotServed.ToList();

        errors.AddRange(UkraineRegions.Validate(served));
        errors.AddRange(UkraineRegions.Validate(notServed));

        foreach (var code in served.Intersect(notServed, StringComparer.OrdinalIgnoreCase).Distinct())
            errors.Add($"Регіон '{code}' вказано одночасно як «обслуговується» та «не обслуговується».");

        foreach (var entry in n.Served)
        {
            if (IsDaysOutOfRange(entry.DeliveryDaysMin) || IsDaysOutOfRange(entry.DeliveryDaysMax))
                errors.Add($"Термін доставки для регіону '{entry.RegionCode}' має бути в межах 0–{MaxDeliveryDays} днів.");

            if (entry.MinOrderAmount is < 0)
                errors.Add($"Мінімальна сума замовлення для регіону '{entry.RegionCode}' не може бути відʼємною.");
        }

        return errors;
    }

    private static bool IsDaysOutOfRange(int? days) => days is < 0 or > MaxDeliveryDays;

    /// <summary>
    /// Trims strings, drops blank region codes, dedupes both lists case-insensitively (first
    /// occurrence wins for <c>served</c>, keeping its fields), and orders each served entry's
    /// day range so <c>min &lt;= max</c> (swapping a reversed pair). Idempotent.
    /// </summary>
    internal static DeliveryCoverageDto Normalize(DeliveryCoverageDto dto)
    {
        var served = new List<DeliveryCoverageEntryDto>();
        var seenServed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dto.Served ?? Array.Empty<DeliveryCoverageEntryDto>())
        {
            if (entry is null)
                continue;
            var code = entry.RegionCode?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !seenServed.Add(code))
                continue;

            var entryNote = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note!.Trim();

            var min = entry.DeliveryDaysMin;
            var max = entry.DeliveryDaysMax;
            if (min.HasValue && max.HasValue && min.Value > max.Value)
                (min, max) = (max, min);

            served.Add(new DeliveryCoverageEntryDto(code!, min, max, entry.MinOrderAmount, entryNote));
        }

        var notServed = new List<string>();
        var seenNot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in dto.NotServed ?? Array.Empty<string>())
        {
            var code = raw?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !seenNot.Add(code))
                continue;
            notServed.Add(code!);
        }

        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim();
        return new DeliveryCoverageDto(served, notServed, note);
    }

    /// <summary>
    /// Maps one raw JSON entry to a DTO, healing the legacy shape: a non-empty <c>terms</c> with
    /// no <c>note</c> becomes the <c>note</c>.
    /// </summary>
    private static DeliveryCoverageEntryDto ToEntry(RawEntry e)
    {
        var note = string.IsNullOrWhiteSpace(e.Note) ? null : e.Note;
        if (note is null && !string.IsNullOrWhiteSpace(e.Terms))
            note = e.Terms;

        return new DeliveryCoverageEntryDto(
            e.RegionCode ?? string.Empty,
            e.DeliveryDaysMin,
            e.DeliveryDaysMax,
            e.MinOrderAmount,
            note);
    }

    private sealed class Raw
    {
        public List<RawEntry>? Served { get; set; }
        public List<string>? NotServed { get; set; }
        public string? Note { get; set; }
    }

    private sealed class RawEntry
    {
        public string? RegionCode { get; set; }
        public int? DeliveryDaysMin { get; set; }
        public int? DeliveryDaysMax { get; set; }
        public decimal? MinOrderAmount { get; set; }
        public string? Note { get; set; }

        /// <summary>Legacy pre-TASK-665 single free-text field. Read-only back-compat — never written.</summary>
        public string? Terms { get; set; }
    }
}
