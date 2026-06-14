namespace ShelfGuard.Application.Features.Admin.Dtos;

// ── Tenant list / detail ────────────────────────────────────────────────────

/// <summary>Full tenant representation with usage statistics.</summary>
public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Plan,
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

/// <summary>POST /api/admin/tenants — create a tenant and its first EnterpriseAdmin.</summary>
public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string Plan,
    string AdminEmail,
    string AdminFullName,
    string AdminPassword);

/// <summary>PATCH /api/admin/tenants/{id}/plan</summary>
public sealed record UpdatePlanRequest(string Plan);

/// <summary>PATCH /api/admin/tenants/{id}/modules</summary>
public sealed record UpdateModulesRequest(IReadOnlyList<string> Modules);
