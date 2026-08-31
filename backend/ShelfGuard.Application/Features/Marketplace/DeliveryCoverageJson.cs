using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// (De)serialization + validation for the <c>supplier_profiles.DeliveryCoverage</c> JSONB string
/// (TASK-650). Canonical stored shape is camelCase, matching both the frontend
/// <c>features/geo</c> <c>DeliveryCoverage</c> type and the plan's documented shape:
/// <code>
/// { "served": [{ "regionCode": "UA-32", "terms": "2-3 дні" }],
///   "notServed": ["UA-43"],
///   "note": "Доставка Новою Поштою за домовленістю" }
/// </code>
/// The same casing convention the rest of the codebase uses for JSONB string columns that are
/// (de)serialized in the application layer (<c>Categories</c> is a bare string array where casing
/// is moot; the worker writes <c>supplier_metrics.DeliveryByRegion</c> in camelCase).
/// </summary>
public static class DeliveryCoverageJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Deserializes the stored JSONB string into a DTO. Returns <c>null</c> for null/blank/invalid
    /// JSON. Tolerates missing keys (absent <c>served</c>/<c>notServed</c> become empty lists).
    /// The result is normalized (trimmed, blanks dropped, deduped) but NOT validated — callers
    /// that accept coverage from the wire must run <see cref="Validate"/> first.
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
                .Select(e => new DeliveryCoverageEntryDto(e.RegionCode ?? string.Empty, e.Terms))
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
                .Select(e => new RawEntry { RegionCode = e.RegionCode, Terms = e.Terms })
                .ToList(),
            NotServed = n.NotServed.ToList(),
            Note = n.Note,
        }, Options);
    }

    /// <summary>
    /// Returns an error message per problem (empty = valid). After normalization: every
    /// <c>served[].regionCode</c> and every <c>notServed</c> entry must pass
    /// <see cref="UkraineRegions.IsValid"/>, and no code may appear in both lists.
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

        return errors;
    }

    /// <summary>
    /// Trims every string, drops blank region codes, and dedupes both lists case-insensitively
    /// (first occurrence wins for <c>served</c>, keeping its terms). Idempotent.
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
            var terms = string.IsNullOrWhiteSpace(entry.Terms) ? null : entry.Terms!.Trim();
            served.Add(new DeliveryCoverageEntryDto(code!, terms));
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

    private sealed class Raw
    {
        public List<RawEntry>? Served { get; set; }
        public List<string>? NotServed { get; set; }
        public string? Note { get; set; }
    }

    private sealed class RawEntry
    {
        public string? RegionCode { get; set; }
        public string? Terms { get; set; }
    }
}
