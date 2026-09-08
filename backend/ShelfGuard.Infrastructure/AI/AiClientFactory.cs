using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Default <see cref="IAiClientFactory"/>. Per-slot resolution (managed-AI Phase 4) for the
/// calling tenant: the slot's enabled <c>integration_configs</c> row (service
/// <c>ai_analyst</c> / <c>ai_assistant</c> / <c>ai_consumer</c>, provider inside the jsonb) →
/// the same row keyless on the shared env key → the <see cref="AiSlot.Analyst"/> row → the
/// <c>Claude:ApiKey</c> / <c>OpenAI:ApiKey</c> env vars → null.
///
/// The <c>integration_configs</c> read is RLS-scoped through the request-scoped
/// <see cref="AppDbContext"/> — no <c>TenantId</c> filter, no RLS-override primitive (enforced
/// by <c>AiAdvisorRlsContainmentTests</c>). <c>(TenantId, Service)</c> is unique, so each slot
/// resolves to at most one row.
/// </summary>
public sealed class AiClientFactory : IAiClientFactory
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string? _envClaudeKey;
    private readonly string _defaultClaudeModel;
    private readonly string? _envOpenAiKey;
    private readonly string _defaultOpenAiModel;

    public AiClientFactory(AppDbContext db, IHttpClientFactory httpFactory, IConfiguration config)
    {
        _db = db;
        _httpFactory = httpFactory;
        _envClaudeKey = config["Claude:ApiKey"];
        _defaultClaudeModel = config["Claude:Model"] ?? "claude-sonnet-4-6";
        _envOpenAiKey = config["OpenAI:ApiKey"];
        _defaultOpenAiModel = config["OpenAI:Model"] ?? "gpt-4o-mini";
    }

    public IAiChatClient Create(AiProviderConfig c) =>
        string.Equals(c.Provider, "openai", StringComparison.OrdinalIgnoreCase)
            ? new OpenAiChatClient(
                _httpFactory.CreateClient("openai"),
                c.ApiKey,
                string.IsNullOrWhiteSpace(c.Model) ? _defaultOpenAiModel : c.Model,
                c.BaseUrl)
            : new AnthropicChatClient(
                c.ApiKey,
                string.IsNullOrWhiteSpace(c.Model) ? _defaultClaudeModel : c.Model);

    public async Task<bool> IsConfiguredAsync(AiSlot slot, CancellationToken ct = default) =>
        await ResolveAsync(slot, ct) is not null;

    public async Task<IAiChatClient?> ResolveAsync(AiSlot slot, CancellationToken ct = default)
    {
        var fromSlot = await ResolveRowAsync(slot, ct);
        if (fromSlot is not null)
            return fromSlot;

        // Slot has no usable row → fall back to the analyst slot's config.
        if (slot != AiSlot.Analyst)
        {
            var fromAnalyst = await ResolveRowAsync(AiSlot.Analyst, ct);
            if (fromAnalyst is not null)
                return fromAnalyst;
        }

        if (!string.IsNullOrWhiteSpace(_envClaudeKey))
            return new AnthropicChatClient(_envClaudeKey!, _defaultClaudeModel);

        if (!string.IsNullOrWhiteSpace(_envOpenAiKey))
            return new OpenAiChatClient(_httpFactory.CreateClient("openai"), _envOpenAiKey!, _defaultOpenAiModel, null);

        return null;
    }

    /// <summary>Builds a client from the slot's own row, or null when the row is missing / has no key and no env key.</summary>
    private async Task<IAiChatClient?> ResolveRowAsync(AiSlot slot, CancellationToken ct)
    {
        var service = slot.ServiceKey();
        var configJson = await _db.IntegrationConfigs
            .Where(i => i.Service == service && i.IsEnabled)
            .Select(i => i.Config)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        var (provider, key, model, baseUrl) = ParseConfig(configJson);
        var resolvedProvider = string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ? "openai" : "claude";

        if (!string.IsNullOrWhiteSpace(key))
            return Create(new AiProviderConfig(resolvedProvider, key!, model ?? "", baseUrl));

        // Keyless row (model / preset / enabled saved, no per-tenant key): run on the shared
        // env key for the row's provider, keeping the row's model + base_url.
        var envKey = resolvedProvider == "openai" ? _envOpenAiKey : _envClaudeKey;
        if (!string.IsNullOrWhiteSpace(envKey))
            return Create(new AiProviderConfig(resolvedProvider, envKey!, model ?? "", baseUrl));

        return null;
    }

    private static (string? Provider, string? Key, string? Model, string? BaseUrl) ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (Str(root, "provider"), Str(root, "api_key"), Str(root, "model"), Str(root, "base_url"));
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
