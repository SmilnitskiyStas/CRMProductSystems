using System.Text.Json;
using System.Text.Json.Nodes;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

/// <inheritdoc />
public sealed class TenantAiConfigService : ITenantAiConfigService
{
    private const string Service = "claude";
    private const string DefaultModel = "claude-sonnet-4-6";

    private readonly IIntegrationService _integrations;
    private readonly IIntegrationRepository _repo;
    private readonly IAiConnectivityTester _tester;

    public TenantAiConfigService(
        IIntegrationService integrations,
        IIntegrationRepository repo,
        IAiConnectivityTester tester)
    {
        _integrations = integrations;
        _repo = repo;
        _tester = tester;
    }

    public async Task<TenantAiAgentDto> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var (config, _) = await _integrations.GetByServiceAsync(tenantId, Service, ct);
        if (config?.Config is null)
            return new TenantAiAgentDto(IsConfigured: false, IsEnabled: false, Model: null,
                ApiKeyLast4: null, ExtraInstructions: null, UpdatedAt: null);

        var maskedKey = (string?)config.Config["api_key"];
        var last4 = PrroSecrets.IsMasked(maskedKey) && maskedKey!.Length > PrroSecrets.MaskToken.Length
            ? maskedKey[PrroSecrets.MaskToken.Length..]
            : null;

        return new TenantAiAgentDto(
            IsConfigured: !string.IsNullOrEmpty(maskedKey),
            IsEnabled: config.IsEnabled,
            Model: (string?)config.Config["model"],
            ApiKeyLast4: last4,
            ExtraInstructions: (string?)config.Config["extra_instructions"],
            UpdatedAt: config.UpdatedAt);
    }

    public async Task<string?> UpdateAsync(Guid tenantId, UpdateAiAgentRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            // Blank key → masked placeholder so IntegrationService keeps the stored key
            // (GenericIntegrationSecrets.MergeMaskedFromStored). A real key is written verbatim.
            ["api_key"] = string.IsNullOrWhiteSpace(request.ApiKey)
                ? PrroSecrets.MaskToken
                : request.ApiKey.Trim(),
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(request.ExtraInstructions))
            payload["extra_instructions"] = request.ExtraInstructions.Trim();

        return await _integrations.UpsertAsync(
            tenantId, Service, new UpsertIntegrationRequest(payload, request.IsEnabled), ct);
    }

    public async Task DeleteAsync(Guid tenantId, CancellationToken ct = default)
        => await _integrations.DeleteAsync(tenantId, Service, ct);

    public async Task<AiAgentTestResult> TestAsync(Guid tenantId, UpdateAiAgentRequest? candidate, CancellationToken ct = default)
    {
        // Raw (unmasked) stored config so a "test the saved key" probe actually has the key.
        var stored = await _repo.GetByServiceAsync(tenantId, Service, ct);
        var (storedKey, storedModel) = ParseStored(stored?.Config);

        var candidateKey = candidate?.ApiKey?.Trim();
        var apiKey = !string.IsNullOrWhiteSpace(candidateKey) && !PrroSecrets.IsMasked(candidateKey)
            ? candidateKey
            : storedKey;

        var model = !string.IsNullOrWhiteSpace(candidate?.Model) ? candidate!.Model!.Trim()
            : storedModel ?? DefaultModel;

        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiAgentTestResult(Ok: false, Model: model, Error: "Не вказано API-ключ.");

        var probe = await _tester.ProbeAsync(apiKey, model, ct);
        return new AiAgentTestResult(probe.Ok, model, probe.Error);
    }

    private static (string? Key, string? Model) ParseStored(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var key = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() : null;
            var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null;
            return (string.IsNullOrWhiteSpace(key) ? null : key, string.IsNullOrWhiteSpace(model) ? null : model);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
