namespace ShelfGuard.Application.Features.Provider.Dtos;

/// <summary>POST /provider/tenants/:id/users — create a new enterprise_admin for a tenant.</summary>
public record CreateTenantUserRequest(
    string FullName,
    string Email,
    string Password);
