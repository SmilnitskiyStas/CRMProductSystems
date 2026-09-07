using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// <see cref="IAiChatClient"/> for Anthropic/Claude — the code previously duplicated in every
/// advisor, lifted out once. Uses the official <c>Anthropic</c> SDK. On an API error the SDK
/// exception propagates with the provider's raw message in <c>.Message</c> (e.g. "Your credit
/// balance is too low…"), which the Application services pattern-match.
/// </summary>
internal sealed class AnthropicChatClient : IAiChatClient
{
    // SDK default is 10 min ×3 retries — bounded to something a synchronous request can wait on.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicChatClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<AiChatResult> CompleteAsync(AiChatRequest req, CancellationToken ct = default)
    {
        var client = new AnthropicClient { ApiKey = _apiKey, Timeout = ApiTimeout };

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = req.MaxTokens,
            System = req.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = req.UserPrompt }],
            OutputConfig = req.JsonSchema is null
                ? null
                : new OutputConfig { Format = new JsonOutputFormat { Schema = ParseSchema(req.JsonSchema) } },
        };

        var response = await client.Messages.Create(parameters, cancellationToken: ct);

        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Claude returned no text content.");

        var tokens = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        return new AiChatResult(text, _model, tokens);
    }

    private static Dictionary<string, JsonElement> ParseSchema(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
