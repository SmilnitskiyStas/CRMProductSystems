using System.Text.Json;
using System.Text.Json.Nodes;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Integrations;

public sealed class IntegrationService : IIntegrationService
{
    private readonly IIntegrationRepository _repo;

    // Known service identifiers â€” validated on write, not on read.
    private static readonly HashSet<string> KnownServices =
        ["telegram", "resend", "webhook", "prro", "iot", "claude"];

    public IntegrationService(IIntegrationRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<IntegrationSummaryDto>> GetAllAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var configs = await _repo.GetAllByTenantAsync(tenantId, ct);
        return configs
            .Select(c => new IntegrationSummaryDto(c.Service, c.IsEnabled, c.UpdatedAt))
            .ToList();
    }

    public async Task<(IntegrationConfigDto? Config, string? Error)> GetByServiceAsync(
        Guid tenantId, string service, CancellationToken ct = default)
    {
        if (!KnownServices.Contains(service))
            return (null, $"Unknown service: '{service}'.");

        var config = await _repo.GetByServiceAsync(tenantId, service, ct);
        if (config is null)
            return (null, null); // 404 â€” not configured yet

        var jsonObj = ParseConfigSafe(config.Config);
        return (new IntegrationConfigDto(config.Id, config.Service, jsonObj, config.IsEnabled, config.UpdatedAt), null);
    }

    public async Task<string?> UpsertAsync(
        Guid tenantId, string service, UpsertIntegrationRequest request, CancellationToken ct = default)
    {
        if (!KnownServices.Contains(service))
            return $"Unknown service: '{service}'. Allowed: {string.Join(", ", KnownServices)}.";

        var configJson = request.Config.ToJsonString();
        await _repo.UpsertAsync(tenantId, service, configJson, request.IsEnabled, ct);
        return null;
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(
        Guid tenantId, string service, CancellationToken ct = default)
    {
        if (!KnownServices.Contains(service))
            return (false, $"Unknown service: '{service}'.");

        var deleted = await _repo.DeleteAsync(tenantId, service, ct);
        return deleted
            ? (true, null)
            : (false, $"Integration '{service}' is not configured for this tenant.");
    }

    // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static JsonObject? ParseConfigSafe(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
