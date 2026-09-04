using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Catalog;

/// <summary>Maps the per-category default columns to/from the wire <see cref="CategoryDefaults"/> record (Slice 2).</summary>
internal static class CategoryDefaultsMapping
{
    /// <summary>Entity → DTO. Returns <c>null</c> when the category defines no defaults at all
    /// (so the tenant-facing <c>CategoryDto</c> stays lean for the common case).</summary>
    public static CategoryDefaults? ToDto(PlatformCategory c)
    {
        if (c.DefaultVatRate is null
            && c.DefaultPerishabilityClass is null
            && c.DefaultManagementType is null
            && c.DefaultItemType is null
            && c.DefaultShelfLifeDays is null)
            return null;

        return new CategoryDefaults(
            c.DefaultVatRate,
            c.DefaultPerishabilityClass,
            c.DefaultManagementType,
            c.DefaultItemType,
            c.DefaultShelfLifeDays);
    }

    /// <summary>Provider view always returns a (possibly all-null) record so the admin form binds cleanly.</summary>
    public static CategoryDefaults ToProviderDto(PlatformCategory c) =>
        ToDto(c) ?? CategoryDefaults.Empty;

    /// <summary>DTO → entity. A <c>null</c> request clears every default.</summary>
    public static void Apply(PlatformCategory c, CategoryDefaults? d)
    {
        c.DefaultVatRate = d?.VatRate;
        c.DefaultPerishabilityClass = Trimmed(d?.PerishabilityClass);
        c.DefaultManagementType = Trimmed(d?.ManagementType)?.ToUpperInvariant();
        c.DefaultItemType = Trimmed(d?.ItemType);
        c.DefaultShelfLifeDays = d?.ShelfLifeDays;
    }

    private static string? Trimmed(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
