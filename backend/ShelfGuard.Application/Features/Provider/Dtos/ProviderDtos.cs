namespace ShelfGuard.Application.Features.Provider.Dtos;

// ── Tenant list / detail ────────────────────────────────────────────────────

/// <summary>Summary row returned by GET /provider/tenants</summary>
public record TenantSummaryDto(
    Guid     Id,
    string   Name,
    string   Slug,
    string   Plan,
    string   BusinessType,
    string[] Modules,
    bool     IsActive,
    DateTime CreatedAt,
    int      UserCount,
    int      StoreCount,
    int      ExpiredBatchCount);

/// <summary>Full detail returned by GET /provider/tenants/:id</summary>
public record TenantDetailDto(
    Guid      Id,
    string    Name,
    string    Slug,
    string    Plan,
    string    BusinessType,
    string[]  Modules,
    bool      IsActive,
    DateTime  CreatedAt,
    int       UserCount,
    int       StoreCount,
    int       ExpiredBatchCount,
    DateTime? LastActivityAt);

// ── Mutations ───────────────────────────────────────────────────────────────

/// <summary>POST /provider/tenants — create a new tenant</summary>
/// <param name="SupplierCategory">
/// TASK-665: for a <c>businessType == "supplier"</c> tenant, its single primary marketplace
/// category (a <c>SupplierItemCategories</c> key). Validated only for supplier tenants; ignored
/// otherwise. Optional — null leaves the supplier profile without a category.
/// </param>
public record CreateTenantRequest(
    string    Name,
    string    Slug,
    string    BusinessType,
    string    Plan,
    string[]? Modules,
    string?   SupplierCategory = null);

/// <summary>PUT /provider/tenants/:id/supplier-category — set/clear a supplier tenant's primary category</summary>
public record SetSupplierCategoryRequest(string? Category);

/// <summary>PUT /provider/tenants/:id/plan</summary>
public record UpdatePlanRequest(string Plan);

/// <summary>PUT /provider/tenants/:id/modules</summary>
public record UpdateModulesRequest(string[] Modules);

// ── Impersonation ───────────────────────────────────────────────────────────

/// <summary>POST /provider/tenants/:id/impersonate → response body</summary>
public record ImpersonateResponse(
    string AccessToken,
    string TenantName,
    Guid   TenantId);

// ── Health ─────────────────────────────────────────────────────────────────

/// <summary>GET /provider/health</summary>
public record ProviderHealthDto(
    int      TotalTenants,
    int      ActiveTenants,
    int      TotalUsers,
    int      TotalExpiredBatches,
    DateTime Timestamp);

// ── Logs ───────────────────────────────────────────────────────────────────

/// <summary>GET /provider/logs — cross-tenant activity log entry</summary>
public record ProviderLogDto(
    Guid     Id,
    string   Action,
    string   EntityType,
    Guid?    EntityId,
    string?  Meta,
    string?  IpAddress,
    Guid     UserId,
    Guid?    TenantId,
    bool     IsImpersonated,
    DateTime CreatedAt);

/// <summary>Query parameters for GET /provider/logs</summary>
public record ProviderLogsQuery(
    Guid?     TenantId   = null,
    Guid?     UserId     = null,
    string?   Action     = null,
    DateTime? DateFrom   = null,
    DateTime? DateTo     = null,
    int       Page       = 1,
    int       PageSize   = 50);

/// <summary>Paginated response for GET /provider/logs</summary>
public record ProviderLogsPageDto(
    IReadOnlyList<ProviderLogDto> Items,
    int Total,
    int Page,
    int PageSize);
