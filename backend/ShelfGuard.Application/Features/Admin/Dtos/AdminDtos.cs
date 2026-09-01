namespace ShelfGuard.Application.Features.Admin.Dtos;

// ── Tenant list / detail ────────────────────────────────────────────────────

/// <summary>Full tenant representation with usage statistics.</summary>
public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Plan,
    string BusinessType,
    IReadOnlyList<string> Modules,
    bool IsActive,
    DateTime CreatedAt,
    TenantUsageDto Usage);

/// <summary>Real-time usage counters for a tenant.</summary>
public sealed record TenantUsageDto(
    int UsersCount,
    int StoresCount,
    int ProductsCount,
    int SalesLast30Days);

// ── Mutations ───────────────────────────────────────────────────────────────

/// <summary>
/// POST /api/admin/tenants — create a tenant and its first EnterpriseAdmin.
/// BusinessType defaults to "retail" when omitted; it determines the tenant's
/// initial module set (ADR-015 — see Tenant.DefaultModulesForBusinessType).
/// </summary>
/// <param name="SupplierCategory">
/// TASK-665: for a <c>BusinessType == "supplier"</c> tenant, its single primary marketplace
/// category (a <c>SupplierItemCategories</c> key). Validated only for supplier tenants; ignored
/// otherwise. Optional.
/// </param>
public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string Plan,
    string AdminEmail,
    string AdminFullName,
    string AdminPassword,
    string? BusinessType = null,
    string? SupplierCategory = null);

/// <summary>PATCH /api/admin/tenants/{id}/plan</summary>
public sealed record UpdatePlanRequest(string Plan);

/// <summary>PATCH /api/admin/tenants/{id}/modules</summary>
public sealed record UpdateModulesRequest(IReadOnlyList<string> Modules);
