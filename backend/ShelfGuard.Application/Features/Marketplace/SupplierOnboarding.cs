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
    public static (Supplier Supplier, SupplierProfile Profile) CreateOwnerManaged(
        Guid tenantId, string tenantName)
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
        return (supplier, profile);
    }
}
