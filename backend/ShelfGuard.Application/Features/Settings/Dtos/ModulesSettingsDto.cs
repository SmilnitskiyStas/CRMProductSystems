namespace ShelfGuard.Application.Features.Settings.Dtos;

/// <summary>GET /api/settings/modules — the calling tenant's business type and enabled modules.</summary>
public sealed record ModulesSettingsDto(
    string BusinessType,
    IReadOnlyList<string> Modules);
