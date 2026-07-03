namespace ShelfGuard.Application.Features.Provider.Dtos;

/// <summary>
/// POST /provider/tenants/:id/users — create the first admin user for a tenant.
/// Role must match the tenant's business type (ADR-016): supplier tenants get
/// supplier_admin, everyone else gets enterprise_admin.
/// </summary>
public record CreateTenantUserRequest(
    string FullName,
    string Email,
    string Password,
    string Role);
