using System.Text.Json;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier self-service onboarding hook (v4.1, ADR-016, TASK-283/289).
/// A tenant with business_type = "supplier" owns exactly one Supplier plus an
/// owner-managed marketplace profile (hidden until the supplier publishes it).
/// Shared by both tenant-creation paths (TenantAdminService, ProviderService)
/// and by the SupplierCabinetService lazy backfill.
/// </summary>
public static class SupplierOnboarding
{
    public const string SupplierBusinessType = "supplier";

    public static bool IsSupplierBusinessType(string? businessType) =>
        businessType == SupplierBusinessType;

    /// <summary>
    /// Builds the Supplier + owner-managed SupplierProfile pair for a supplier tenant.
    /// Persistence (and transaction scope) is the caller's responsibility.
    /// </summary>
    /// <param name="primaryCategory">
    /// TASK-665: the supplier's single primary category, chosen at tenant creation and read-only
    /// afterward. When it is a valid <see cref="SupplierItemCategories"/> key the profile's
    /// <c>Categories</c> is seeded with that one entry; an invalid or null/blank value leaves
    /// <c>Categories</c> null (the pre-TASK-665 behaviour). Callers validate + surface the error
    /// before reaching here — an unknown key here is simply ignored, not thrown.
    /// </param>
    public static (Supplier Supplier, SupplierProfile Profile) CreateOwnerManaged(
        Guid tenantId, string tenantName, string? primaryCategory = null)
    {
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name     = tenantName,
        };
        var profile = new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = tenantId,
            IsOwnerManaged = true,
            IsPublic       = false,
        };

        if (!string.IsNullOrWhiteSpace(primaryCategory) &&
            SupplierItemCategories.Find(primaryCategory) is not null)
        {
            profile.Categories = JsonSerializer.Serialize(new[] { primaryCategory });
        }

        return (supplier, profile);
    }
}
