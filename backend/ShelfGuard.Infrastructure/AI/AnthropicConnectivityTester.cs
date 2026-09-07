using Anthropic;
using Anthropic.Models.Messages;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// <see cref="IAiConnectivityTester"/> for Anthropic/Claude. Does one minimal 1-token
/// <c>Messages.Create</c> — enough to surface an invalid key, an exhausted credit balance, or a
/// network failure. Counts as the provider's own usage, not the tenant's. Never throws.
/// </summary>
public sealed class AnthropicConnectivityTester : IAiConnectivityTester
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public async Task<AiProbeResult> ProbeAsync(string apiKey, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new AiProbeResult(false, "Не вказано API-ключ.");

        try
        {
            var client = new AnthropicClient { ApiKey = apiKey, Timeout = ProbeTimeout };

            await client.Messages.Create(
                new MessageCreateParams
                {
                    Model = string.IsNullOrWhiteSpace(model) ? "claude-sonnet-4-6" : model,
                    MaxTokens = 1,
                    Messages = [new() { Role = Role.User, Content = "ping" }],
                },
                cancellationToken: ct);

            return new AiProbeResult(true, null);
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
                ? "Недостатньо кредитів на рахунку Anthropic."
                : $"Не вдалося підключитися: {ex.Message}";
            return new AiProbeResult(false, msg);
        }
    }
}
