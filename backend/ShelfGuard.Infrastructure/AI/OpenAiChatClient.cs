using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// <see cref="IAiChatClient"/> for OpenAI and any OpenAI-compatible chat endpoint (Azure OpenAI,
/// Codex, self-hosted) — a thin HttpClient over <c>POST {baseUrl}/chat/completions</c>, no SDK.
/// The base URL defaults to <c>https://api.openai.com/v1</c>; a tenant can override it.
/// On a non-2xx response it throws <see cref="InvalidOperationException"/> whose message is the
/// provider's <c>error.message</c> verbatim (so the "quota"/"invalid_api_key" branches work).
/// </summary>
internal sealed class OpenAiChatClient : IAiChatClient
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public OpenAiChatClient(HttpClient http, string apiKey, string model, string? baseUrl)
    {
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl!).TrimEnd('/');
    }

    public async Task<AiChatResult> CompleteAsync(AiChatRequest req, CancellationToken ct = default)
    {
        var messages = new List<object>(2);
        if (!string.IsNullOrEmpty(req.SystemPrompt))
            messages.Add(new { role = "system", content = req.SystemPrompt });
        messages.Add(new { role = "user", content = req.UserPrompt });

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["max_tokens"] = req.MaxTokens,
        };

        if (req.JsonSchema is not null)
        {
            body["response_format"] = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "response",
                    strict = true,
                    schema = JsonSerializer.Deserialize<JsonElement>(req.JsonSchema),
                },
            };
        }

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var resp = await _http.SendAsync(httpReq, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractError(payload, resp.StatusCode));

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var message = root.GetProperty("choices")[0].GetProperty("message");
        var text = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrEmpty(text))
        {
            var refusal = message.TryGetProperty("refusal", out var r) ? r.GetString() : null;
            throw new InvalidOperationException(refusal ?? "OpenAI returned no text content.");
        }

        var usage = root.GetProperty("usage");
        var tokens = usage.GetProperty("prompt_tokens").GetInt32() + usage.GetProperty("completion_tokens").GetInt32();

        return new AiChatResult(text, _model, tokens);
    }

    private static string ExtractError(string payload, HttpStatusCode status)
    {
        try
        {
            var err = JsonDocument.Parse(payload).RootElement.GetProperty("error");
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            return string.IsNullOrWhiteSpace(msg) ? $"OpenAI error {(int)status}" : msg!;
        }
        catch
        {
            return $"OpenAI error {(int)status}";
        }
    }
}
