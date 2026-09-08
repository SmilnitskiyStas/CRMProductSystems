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
    private const string Claude = "claude";
    private const string OpenAi = "openai";
    private const string DefaultClaudeModel = "claude-sonnet-4-6";
    private const string DefaultOpenAiModel = "gpt-4o-mini";

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

    private static string Normalize(string? provider) =>
        string.Equals(provider, OpenAi, StringComparison.OrdinalIgnoreCase) ? OpenAi : Claude;

    private static string DefaultModel(string provider) => provider == OpenAi ? DefaultOpenAiModel : DefaultClaudeModel;

    private static string SlotName(AiSlot slot) => slot.ToString().ToLowerInvariant();

    public async Task<IReadOnlyList<TenantAiAgentDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var list = new List<TenantAiAgentDto>(AiSlots.All.Count);
        foreach (var slot in AiSlots.All)
            list.Add(await GetAsync(tenantId, slot, ct));
        return list;
    }

    public async Task<TenantAiAgentDto> GetAsync(Guid tenantId, AiSlot slot, CancellationToken ct = default)
    {
        var (config, _) = await _integrations.GetByServiceAsync(tenantId, slot.ServiceKey(), ct);
        if (config?.Config is null)
            return new TenantAiAgentDto(SlotName(slot), false, false, null, null, null, null, null, null);

        // A row without a per-tenant key is still a real config — the model, preset
        // (extra_instructions) and enabled flag were saved and the agent runs on the
        // provider's shared env key (AiClientFactory).
        var maskedKey = (string?)config.Config["api_key"];
        var last4 = !string.IsNullOrEmpty(maskedKey)
                    && PrroSecrets.IsMasked(maskedKey!)
                    && maskedKey!.Length > PrroSecrets.MaskToken.Length
            ? maskedKey![PrroSecrets.MaskToken.Length..]
            : null;

        return new TenantAiAgentDto(
            Slot: SlotName(slot),
            IsConfigured: true,
            IsEnabled: config.IsEnabled,
            Provider: Normalize((string?)config.Config["provider"]),
            Model: (string?)config.Config["model"],
            ApiKeyLast4: last4,
            BaseUrl: (string?)config.Config["base_url"],
            ExtraInstructions: (string?)config.Config["extra_instructions"],
            UpdatedAt: config.UpdatedAt);
    }

    public async Task<string?> UpdateAsync(Guid tenantId, AiSlot slot, UpdateAiAgentRequest request, CancellationToken ct = default)
    {
        var provider = Normalize(request.Provider);

        var payload = new JsonObject
        {
            ["provider"] = provider,
            // Blank key → masked placeholder so IntegrationService keeps the stored key
            // (GenericIntegrationSecrets.MergeMaskedFromStored). A real key is written verbatim.
            ["api_key"] = string.IsNullOrWhiteSpace(request.ApiKey)
                ? PrroSecrets.MaskToken
                : request.ApiKey.Trim(),
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel(provider) : request.Model.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(request.ExtraInstructions))
            payload["extra_instructions"] = request.ExtraInstructions.Trim();

        if (provider == OpenAi && !string.IsNullOrWhiteSpace(request.BaseUrl))
            payload["base_url"] = request.BaseUrl.Trim();

        return await _integrations.UpsertAsync(
            tenantId, slot.ServiceKey(), new UpsertIntegrationRequest(payload, request.IsEnabled), ct);
    }

    public Task DeleteAsync(Guid tenantId, AiSlot slot, CancellationToken ct = default) =>
        _integrations.DeleteAsync(tenantId, slot.ServiceKey(), ct);

    public async Task<AiAgentTestResult> TestAsync(Guid tenantId, AiSlot slot, UpdateAiAgentRequest? candidate, CancellationToken ct = default)
    {
        // Raw (unmasked) stored config so a "test the saved key" probe actually has the key.
        var stored = await _repo.GetByServiceAsync(tenantId, slot.ServiceKey(), ct);
        var (storedProvider, storedKey, storedModel, storedBaseUrl) = ParseStored(stored?.Config);

        var provider = Normalize(candidate?.Provider ?? storedProvider);

        var candidateKey = candidate?.ApiKey?.Trim();
        var apiKey = !string.IsNullOrWhiteSpace(candidateKey) && !PrroSecrets.IsMasked(candidateKey)
            ? candidateKey
            : storedKey;

        var model = !string.IsNullOrWhiteSpace(candidate?.Model) ? candidate!.Model!.Trim()
            : storedModel ?? DefaultModel(provider);

        var baseUrl = !string.IsNullOrWhiteSpace(candidate?.BaseUrl) ? candidate!.BaseUrl!.Trim() : storedBaseUrl;

        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiAgentTestResult(Ok: false, Model: model, Error: "Не вказано API-ключ.");

        var probe = await _tester.ProbeAsync(new AiProviderConfig(provider, apiKey, model, baseUrl), ct);
        return new AiAgentTestResult(probe.Ok, model, probe.Error);
    }

    private static (string? Provider, string? Key, string? Model, string? BaseUrl) ParseStored(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return (null, null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            return (Str(root, "provider"), Str(root, "api_key"), Str(root, "model"), Str(root, "base_url"));
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;
}
