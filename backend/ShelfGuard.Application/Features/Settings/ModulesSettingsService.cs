using ShelfGuard.Application.Features.Settings.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Settings;

public sealed class ModulesSettingsService : IModulesSettingsService
{
    private readonly ITenantRepository _repo;

    public ModulesSettingsService(ITenantRepository repo) => _repo = repo;

    public async Task<ModulesSettingsDto?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _repo.GetByIdAsync(tenantId, ct);
        return tenant is null ? null : new ModulesSettingsDto(tenant.BusinessType, tenant.GetModules());
    }
}
