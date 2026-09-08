using System.Text.Json;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ConsumerAssistant;

/// <inheritdoc />
public sealed class ConsumerAssistantService : IConsumerAssistantService
{
    private const int MaxTokens = 1024;
    private const int ExcerptLength = 200;
    private const string AiConsumerService = "ai_consumer";

    private const string BaseSystemPrompt =
        "Ти — AI-консультант у застосунку лояльності. Допомагай клієнту з питаннями про його " +
        "бонусний рахунок, рівень, історію покупок, наявний каталог і магазини мережі. " +
        "Відповідай коротко (2–4 речення), українською, дружньо й зрозуміло. Якщо потрібних " +
        "даних немає в наданому контексті — чесно про це скажи, не вигадуй цифри.";

    private readonly ITenantSessionOverride _tenantScope;
    private readonly IIntegrationRepository _integrations;
    private readonly ITenantRepository _tenants;
    private readonly ILoyaltyService _loyalty;
    private readonly IConsumerContentService _content;
    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;
    private readonly IConsumerAiRequestRepository _audit;

    public ConsumerAssistantService(
        ITenantSessionOverride tenantScope,
        IIntegrationRepository integrations,
        ITenantRepository tenants,
        ILoyaltyService loyalty,
        IConsumerContentService content,
        IAiClientFactory ai,
        IAiPromptResolver prompt,
        IConsumerAiRequestRepository audit)
    {
        _tenantScope = tenantScope;
        _integrations = integrations;
        _tenants = tenants;
        _loyalty = loyalty;
        _content = content;
        _ai = ai;
        _prompt = prompt;
        _audit = audit;
    }

    public async Task<(ConsumerAssistantAnswer? Answer, string? Error, int? StatusCode)> AskAsync(
        Guid consumerAccountId, Guid tenantId, string message, CancellationToken ct = default)
    {
        message = message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return (null, "Порожнє повідомлення.", 400);
        if (message.Length > 2000)
            message = message[..2000];

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, "Магазин не знайдено.", 404);

        // ── 1. ai_consumer slot config — a consumer session can't read integration_configs
        //       under RLS, so read it through the sanctioned tenant-scope override. ──────
        var configJson = await _tenantScope.ExecuteAsync(tenantId, async () =>
        {
            var row = await _integrations.GetByServiceAsync(tenantId, AiConsumerService, ct);
            return row is { IsEnabled: true } ? row.Config : null;
        }, ct);

        if (string.IsNullOrWhiteSpace(configJson))
            return (null, "AI-консультант недоступний для цього магазину.", 404);

        var cfg = ParseConfig(configJson);
        var apiKey = string.IsNullOrWhiteSpace(cfg.ApiKey) || PrroSecrets.IsMasked(cfg.ApiKey!)
            ? null
            : cfg.ApiKey;
        var client = _ai.CreateOrEnv(cfg.Provider, apiKey, cfg.Model, cfg.BaseUrl);
        if (client is null)
            return (null, "AI-консультант недоступний для цього магазину.", 404);

        // ── 2. this consumer's own data for this tenant (all consumer-safe direct calls) ──
        var memberships = await _loyalty.GetMembershipsForConsumerAsync(consumerAccountId, ct);
        var membership = memberships.FirstOrDefault(m => m.TenantId == tenantId);

        var (tierProgress, _, _) = await _loyalty.GetTierProgressAsync(consumerAccountId, tenantId, ct);
        var (ladder, _, _) = await _loyalty.GetTierLadderForConsumerAsync(consumerAccountId, tenantId, ct);
        var (history, _, _) = await _loyalty.GetHistoryAsync(consumerAccountId, tenantId, 1, 10, ct);

        var networks = await _loyalty.GetNetworksForConsumerAsync(consumerAccountId, ct);
        var network = networks.FirstOrDefault(n => n.TenantId == tenantId);

        // Public catalog for the consumer's preferred store, ranked by their question.
        // GetCatalogAsync opens its own tenant-scope transaction — never call it from inside one.
        object catalog = Array.Empty<object>();
        if (membership?.PreferredStoreId is { } storeId)
        {
            var (page, _) = await _content.GetCatalogAsync(tenantId, storeId, message, null, 1, 15, ct);
            if (page?.Items is { Count: > 0 } items)
                catalog = items;
        }

        // ── 3. prompt: hardened consumer guardrail + provider extra_instructions + base ──
        var system = _prompt.Compose(tenant.Name, cfg.ExtraInstructions, BaseSystemPrompt, AiSlot.Consumer);
        var user = BuildUserPrompt(message, membership, tierProgress, ladder, history?.Items, network, catalog);

        AiChatResult result;
        try
        {
            result = await client.CompleteAsync(new AiChatRequest(system, user, MaxTokens), ct);
        }
        catch (Exception)
        {
            return (null, "Не вдалося отримати відповідь від консультанта. Спробуйте пізніше.", 502);
        }

        var text = result.Text.Trim();

        // ── 4. audit (metadata + short excerpts) — insert under tenant_isolation ──────────
        await _tenantScope.ExecuteAsync(tenantId, async () =>
        {
            await _audit.AddAsync(new ConsumerAiRequest
            {
                TenantId = tenantId,
                ConsumerAccountId = consumerAccountId,
                PromptExcerpt = Excerpt(message),
                ResponseExcerpt = Excerpt(text),
                Model = result.Model,
                TokensUsed = result.TokensUsed,
            }, ct);
            return 0;
        }, ct);

        return (new ConsumerAssistantAnswer(text, result.Model, result.TokensUsed), null, null);
    }

    private static string Excerpt(string s) =>
        s.Length <= ExcerptLength ? s : s[..ExcerptLength];

    private static (string Provider, string? ApiKey, string? Model, string? BaseUrl, string? ExtraInstructions) ParseConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var provider = Str(root, "provider");
            return (
                string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ? "openai" : "claude",
                Str(root, "api_key"),
                Str(root, "model"),
                Str(root, "base_url"),
                Str(root, "extra_instructions"));
        }
        catch (JsonException)
        {
            return ("claude", null, null, null, null);
        }
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static string BuildUserPrompt(
        string message,
        LoyaltyMembershipSummaryDto? membership,
        object? tierProgress,
        object? tierLadder,
        object? history,
        object? network,
        object catalog)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        return
            $"ЗАПИТ КЛІЄНТА: {message}\n\n" +
            "=== КОНТЕКСТ (лише дані цього клієнта + публічна інформація мережі) ===\n\n" +
            "МОЄ ЧЛЕНСТВО В ПРОГРАМІ:\n" +
            $"{JsonSerializer.Serialize(membership, opts)}\n\n" +
            "МІЙ РІВЕНЬ (прогрес):\n" +
            $"{JsonSerializer.Serialize(tierProgress, opts)}\n\n" +
            "ДРАБИНА РІВНІВ ПРОГРАМИ:\n" +
            $"{JsonSerializer.Serialize(tierLadder, opts)}\n\n" +
            "МОЯ ІСТОРІЯ БОНУСІВ (останні 10):\n" +
            $"{JsonSerializer.Serialize(history, opts)}\n\n" +
            "МЕРЕЖА ТА МАГАЗИНИ:\n" +
            $"{JsonSerializer.Serialize(network, opts)}\n\n" +
            "КАТАЛОГ (релевантні позиції з наявністю):\n" +
            $"{JsonSerializer.Serialize(catalog, opts)}\n\n" +
            "Дай корисну коротку відповідь на запит клієнта на основі цього контексту.";
    }
}
