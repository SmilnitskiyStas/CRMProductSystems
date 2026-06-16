using ShelfGuard.Application.Features.Settings.Dtos;

namespace ShelfGuard.Application.Features.Settings;

/// <summary>Read-only module/business-type info for the calling tenant (enterprise_admin self-service).</summary>
public interface IModulesSettingsService
{
    Task<ModulesSettingsDto?> GetAsync(Guid tenantId, CancellationToken ct = default);
}
