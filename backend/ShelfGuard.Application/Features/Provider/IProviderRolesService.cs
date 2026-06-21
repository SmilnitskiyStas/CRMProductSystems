using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderRolesService
{
    Task<IReadOnlyList<ProviderRoleDto>> GetAllAsync(CancellationToken ct);
    Task<(ProviderRoleDto? Role, string? Error)> CreateAsync(CreateProviderRoleRequest req, CancellationToken ct);
    Task<(ProviderRoleDto? Role, string? Error)> UpdateAsync(Guid id, UpdateProviderRoleRequest req, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct);
}
