using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Default <see cref="IAiClientFactory"/>. Resolution order for the calling tenant:
/// its enabled <c>integration_configs</c> AI row (service 'openai' or 'claude') →
/// <c>Claude:ApiKey</c> env → <c>OpenAI:ApiKey</c> env → null.
///
/// The <c>integration_configs</c> read is RLS-scoped through the request-scoped
/// <see cref="AppDbContext"/> exactly like the advisors' old <c>ResolveAsync</c> — no
/// <c>TenantId</c> filter, no RLS-override primitive (enforced by
/// <c>AiAdvisorRlsContainmentTests</c>). <c>TenantAiConfigService</c> keeps at most one AI
/// row enabled per tenant, so the <c>OrderBy(Service)</c> is only a tiebreaker for a
/// misconfigured state (prefers 'claude').
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

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        await ResolveAsync(ct) is not null;

    public async Task<IAiChatClient?> ResolveAsync(CancellationToken ct = default)
    {
        var row = await _db.IntegrationConfigs
            .Where(i => (i.Service == "openai" || i.Service == "claude") && i.IsEnabled)
            .OrderBy(i => i.Service) // "claude" < "openai" — deterministic tiebreaker
            .Select(i => new { i.Service, i.Config })
            .FirstOrDefaultAsync(ct);

        var isOpenAiRow = row is not null && string.Equals(row.Service, "openai", StringComparison.OrdinalIgnoreCase);

        if (row is not null)
        {
            var (key, model, baseUrl) = ParseConfig(row.Config);
            if (!string.IsNullOrWhiteSpace(key))
                return Create(new AiProviderConfig(row.Service, key!, model ?? "", baseUrl));

            // Keyless row (the provider saved model/preset/enabled but no per-tenant key):
            // run on the shared env key, but keep the row's provider + model + base_url choice.
            var envKeyForRow = isOpenAiRow ? _envOpenAiKey : _envClaudeKey;
            if (!string.IsNullOrWhiteSpace(envKeyForRow))
                return Create(new AiProviderConfig(row.Service, envKeyForRow!, model ?? "", baseUrl));
        }

        if (!string.IsNullOrWhiteSpace(_envClaudeKey))
            return new AnthropicChatClient(_envClaudeKey!, _defaultClaudeModel);

        if (!string.IsNullOrWhiteSpace(_envOpenAiKey))
            return new OpenAiChatClient(_httpFactory.CreateClient("openai"), _envOpenAiKey!, _defaultOpenAiModel, null);

        return null;
    }

    private static (string? Key, string? Model, string? BaseUrl) ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (Str(root, "api_key"), Str(root, "model"), Str(root, "base_url"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
