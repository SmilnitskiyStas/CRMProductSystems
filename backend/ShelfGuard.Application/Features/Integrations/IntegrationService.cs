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
        ["telegram", "resend", "webhook", "prro", "iot", "claude", "vchasno"];

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

        // ПРРО secrets are write-only (ADR-013 p.4) — the generic read path masks them too.
        if (service == "prro" && jsonObj is not null)
            PrroSecrets.MaskInPlace(jsonObj);

        // Вчасно API key is write-only too (TASK-317) — same masking convention.
        if (service == "vchasno" && jsonObj is not null)
            VchasnoSecrets.MaskInPlace(jsonObj);

        // All other known services (claude/telegram/resend/webhook/iot) carry secrets too —
        // mask those the same way (TASK-347: "integrations.view", ADR-020, first made this
        // endpoint reachable by a staff-rank capability holder, exposing them unmasked).
        if (jsonObj is not null)
            GenericIntegrationSecrets.MaskInPlace(service, jsonObj);

        return (new IntegrationConfigDto(config.Id, config.Service, jsonObj, config.IsEnabled, config.UpdatedAt), null);
    }

    public async Task<string?> UpsertAsync(
        Guid tenantId, string service, UpsertIntegrationRequest request, CancellationToken ct = default)
    {
        if (!KnownServices.Contains(service))
            return $"Unknown service: '{service}'. Allowed: {string.Join(", ", KnownServices)}.";

        // A round-tripped masked GET payload must never overwrite stored ПРРО secrets.
        if (service == "prro")
        {
            var existing = await _repo.GetByServiceAsync(tenantId, service, ct);
            PrroSecrets.MergeMaskedFromStored(
                request.Config,
                existing is null ? null : ParseConfigSafe(existing.Config));
        }

        // Same round-trip protection for the Вчасно API key (TASK-317).
        if (service == "vchasno")
        {
            var existing = await _repo.GetByServiceAsync(tenantId, service, ct);
            VchasnoSecrets.MergeMaskedFromStored(
                request.Config,
                existing is null ? null : ParseConfigSafe(existing.Config));
        }

        // Same round-trip protection for the other services now masked on GET (TASK-347) —
        // without this, saving an unrelated field (e.g. toggling isEnabled) after a GET would
        // write the literal "••••xxxx" placeholder over the real stored secret.
        if (GenericIntegrationSecrets.HasSecretField(service))
        {
            var existing = await _repo.GetByServiceAsync(tenantId, service, ct);
            GenericIntegrationSecrets.MergeMaskedFromStored(
                service, request.Config,
                existing is null ? null : ParseConfigSafe(existing.Config));
        }

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
