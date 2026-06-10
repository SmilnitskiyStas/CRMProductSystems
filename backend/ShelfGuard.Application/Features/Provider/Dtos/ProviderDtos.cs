namespace ShelfGuard.Application.Features.Provider.Dtos;

// ── Tenant list / detail ────────────────────────────────────────────────────

/// <summary>Summary row returned by GET /provider/tenants</summary>
public record TenantSummaryDto(
    Guid     Id,
    string   Name,
    string   Slug,
    string   Plan,
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
    string[]  Modules,
    bool      IsActive,
    DateTime  CreatedAt,
    int       UserCount,
    int       StoreCount,
    int       ExpiredBatchCount,
    DateTime? LastActivityAt);

// ── Mutations ───────────────────────────────────────────────────────────────

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
    DateTime CreatedAt);
