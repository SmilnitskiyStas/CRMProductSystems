using System.Net;
using System.Text.Json;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.AI;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

/// <summary>
/// Managed-AI Phase 2 — the thin OpenAI-compatible chat client.
/// </summary>
public sealed class OpenAiChatClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public FakeHandler(HttpStatusCode status, string json) { _status = status; _json = json; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (OpenAiChatClient Client, FakeHandler Handler) Build(HttpStatusCode status, string json, string? baseUrl = null)
    {
        var handler = new FakeHandler(status, json);
        return (new OpenAiChatClient(new HttpClient(handler), "sk-test", "gpt-4o-mini", baseUrl), handler);
    }

    private const string OkResponse = """
        {
          "choices": [ { "message": { "role": "assistant", "content": "Привіт!" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 12, "completion_tokens": 3, "total_tokens": 15 }
        }
        """;

    [Fact]
    public async Task PlainText_sends_the_expected_body_and_parses_the_reply()
    {
        var (client, handler) = Build(HttpStatusCode.OK, OkResponse);

        var result = await client.CompleteAsync(new AiChatRequest("Ти — асистент.", "Привітайся", MaxTokens: 128));

        Assert.Equal("Привіт!", result.Text);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.Equal(15, result.TokensUsed);

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization!.Parameter);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(128, body.RootElement.GetProperty("max_tokens").GetInt32());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Structured_output_adds_response_format_json_schema()
    {
        var (client, handler) = Build(HttpStatusCode.OK, OkResponse);
        const string schema = """{ "type": "object", "properties": { "x": { "type": "number" } }, "required": ["x"], "additionalProperties": false }""";

        await client.CompleteAsync(new AiChatRequest("s", "u", MaxTokens: 64, JsonSchema: schema));

        using var body = JsonDocument.Parse(handler.LastBody!);
        var rf = body.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", rf.GetProperty("type").GetString());
        Assert.True(rf.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("object", rf.GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Custom_base_url_is_honoured()
    {
        var (client, handler) = Build(HttpStatusCode.OK, OkResponse, baseUrl: "https://my-proxy.example/v1/");

        await client.CompleteAsync(new AiChatRequest("s", "u", MaxTokens: 8));

        Assert.Equal("https://my-proxy.example/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Non_2xx_throws_with_the_providers_raw_error_message()
    {
        const string err = """{ "error": { "message": "Incorrect API key provided.", "type": "invalid_request_error", "code": "invalid_api_key" } }""";
        var (client, _) = Build(HttpStatusCode.Unauthorized, err);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new AiChatRequest("s", "u", MaxTokens: 8)));

        Assert.Equal("Incorrect API key provided.", ex.Message);
    }

    [Fact]
    public async Task Null_content_surfaces_the_refusal()
    {
        const string refusal = """
            { "choices": [ { "message": { "role": "assistant", "content": null, "refusal": "I can't help with that." } } ],
              "usage": { "prompt_tokens": 5, "completion_tokens": 0, "total_tokens": 5 } }
            """;
        var (client, _) = Build(HttpStatusCode.OK, refusal);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new AiChatRequest("s", "u", MaxTokens: 8)));

        Assert.Equal("I can't help with that.", ex.Message);
    }
}
