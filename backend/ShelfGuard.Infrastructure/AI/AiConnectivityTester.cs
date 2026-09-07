using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Default <see cref="IAiConnectivityTester"/> (managed-AI Phase 2). Builds a client for the
/// candidate credentials via <see cref="IAiClientFactory.Create"/> and does one minimal
/// 1-token completion — enough to surface an invalid key, an exhausted quota / credit balance,
/// or a network failure, for either provider (Claude or OpenAI-compatible). Counts as the
/// provider's own usage, not the tenant's. Never throws.
/// </summary>
public sealed class AiConnectivityTester : IAiConnectivityTester
{
    private readonly IAiClientFactory _factory;

    public AiConnectivityTester(IAiClientFactory factory) => _factory = factory;

    public async Task<AiProbeResult> ProbeAsync(AiProviderConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return new AiProbeResult(false, "Не вказано API-ключ.");

        try
        {
            var client = _factory.Create(config);
            await client.CompleteAsync(new AiChatRequest("Тест підключення.", "ping", MaxTokens: 1), ct);
            return new AiProbeResult(true, null);
        }
        catch (Exception ex)
        {
            return new AiProbeResult(false, Humanize(ex.Message));
        }
    }

    private static string Humanize(string raw)
    {
        if (raw.Contains("credit balance", StringComparison.OrdinalIgnoreCase))
            return "Недостатньо кредитів на рахунку Anthropic.";
        if (raw.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return "Вичерпано квоту / кредит на рахунку OpenAI.";
        if (raw.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("authentication_error", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Incorrect API key", StringComparison.OrdinalIgnoreCase))
            return "Невірний API-ключ.";
        return $"Не вдалося підключитися: {raw}";
    }
}
