using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.AI;
using ShelfGuard.Infrastructure.Data;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

/// <summary>
/// Managed-AI Phase 2 (provider resolution) + Phase 4 (per-slot). RLS scoping is covered by
/// <see cref="ShelfGuard.Tests.Infrastructure.AiIntegrationConfigCrossTenantRlsIntegrationTests"/>
/// (real Postgres); this exercises the routing logic on EF InMemory.
/// </summary>
public sealed class AiClientFactoryTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ai-factory-{Guid.NewGuid()}").Options);

    private static IHttpClientFactory HttpFactory()
    {
        var f = Substitute.For<IHttpClientFactory>();
        f.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        return f;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.ToDictionary(x => x.Key, x => x.Value))
            .Build();

    /// <summary>Seeds a slot row. <paramref name="service"/> is a slot key ("ai_analyst" etc.).</summary>
    private static void Seed(AppDbContext db, string service, string configJson, bool enabled = true)
    {
        db.IntegrationConfigs.Add(new IntegrationConfig
        {
            TenantId = Guid.NewGuid(),
            Service = service,
            Config = configJson,
            IsEnabled = enabled,
        });
        db.SaveChanges();
    }

    [Fact]
    public void Create_routes_openai_to_the_thin_client_and_claude_to_the_sdk_client()
    {
        var sut = new AiClientFactory(NewDb(), HttpFactory(), Config());

        Assert.IsType<OpenAiChatClient>(sut.Create(new AiProviderConfig("openai", "k", "gpt-4o-mini")));
        Assert.IsType<AnthropicChatClient>(sut.Create(new AiProviderConfig("claude", "k", "claude-sonnet-4-6")));
        // unknown provider → Anthropic (safe default)
        Assert.IsType<AnthropicChatClient>(sut.Create(new AiProviderConfig("something", "k", "m")));
    }

    [Fact]
    public void CreateOrEnv_uses_the_explicit_key_then_the_provider_env_key_then_null()
    {
        var withEnv = new AiClientFactory(NewDb(), HttpFactory(),
            Config(("Claude:ApiKey", "sk-ant-env"), ("OpenAI:ApiKey", "sk-oai-env")));

        Assert.IsType<AnthropicChatClient>(withEnv.CreateOrEnv("claude", "sk-explicit", "m", null));
        Assert.IsType<AnthropicChatClient>(withEnv.CreateOrEnv("claude", null, "m", null));   // → claude env
        Assert.IsType<OpenAiChatClient>(withEnv.CreateOrEnv("openai", "  ", "m", null));       // → openai env

        var noEnv = new AiClientFactory(NewDb(), HttpFactory(), Config());
        Assert.Null(noEnv.CreateOrEnv("claude", null, "m", null));
    }

    [Fact]
    public async Task Resolve_reads_the_slot_row_and_its_provider_from_the_jsonb()
    {
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"provider":"openai","api_key":"sk-openai","model":"gpt-4o"}""");
        var sut = new AiClientFactory(db, HttpFactory(), Config());

        Assert.IsType<OpenAiChatClient>(await sut.ResolveAsync(AiSlot.Analyst));
        Assert.True(await sut.IsConfiguredAsync(AiSlot.Analyst));
    }

    [Fact]
    public async Task Resolve_defaults_to_claude_when_the_jsonb_has_no_provider()
    {
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"api_key":"sk-ant","model":"claude-sonnet-4-6"}""");
        var sut = new AiClientFactory(db, HttpFactory(), Config());

        Assert.IsType<AnthropicChatClient>(await sut.ResolveAsync(AiSlot.Analyst));
    }

    [Fact]
    public async Task Resolve_ignores_a_disabled_slot_row()
    {
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"provider":"claude","api_key":"sk-ant"}""", enabled: false);
        var sut = new AiClientFactory(db, HttpFactory(), Config());

        Assert.Null(await sut.ResolveAsync(AiSlot.Analyst));
    }

    [Fact]
    public async Task Resolve_falls_back_from_the_assistant_slot_to_the_analyst_slot()
    {
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"provider":"claude","api_key":"sk-ant","model":"claude-opus-4-1"}""");
        var sut = new AiClientFactory(db, HttpFactory(), Config());

        // assistant slot has no row → uses the analyst slot's config
        Assert.IsType<AnthropicChatClient>(await sut.ResolveAsync(AiSlot.Assistant));
        Assert.True(await sut.IsConfiguredAsync(AiSlot.Assistant));
    }

    [Fact]
    public async Task Resolve_keyless_slot_row_with_no_env_key_is_null()
    {
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"provider":"openai","model":"gpt-4o"}""");
        var sut = new AiClientFactory(db, HttpFactory(), Config());

        Assert.Null(await sut.ResolveAsync(AiSlot.Analyst));
    }

    [Fact]
    public async Task Resolve_keyless_slot_row_falls_back_to_the_env_key_for_that_provider()
    {
        // Provider saved a model/preset but no per-tenant key → run on the shared env key,
        // keeping the row's provider choice (openai here, not the claude env key).
        var db = NewDb();
        Seed(db, AiSlots.AnalystService, """{"provider":"openai","model":"gpt-4o"}""");
        var sut = new AiClientFactory(db, HttpFactory(),
            Config(("Claude:ApiKey", "sk-ant-env"), ("OpenAI:ApiKey", "sk-oai-env")));

        Assert.IsType<OpenAiChatClient>(await sut.ResolveAsync(AiSlot.Analyst));
    }

    [Fact]
    public async Task Resolve_falls_back_to_the_claude_env_key_then_null()
    {
        var withEnv = new AiClientFactory(NewDb(), HttpFactory(), Config(("Claude:ApiKey", "sk-env")));
        Assert.IsType<AnthropicChatClient>(await withEnv.ResolveAsync(AiSlot.Analyst));

        var withOpenAiEnv = new AiClientFactory(NewDb(), HttpFactory(), Config(("OpenAI:ApiKey", "sk-oai-env")));
        Assert.IsType<OpenAiChatClient>(await withOpenAiEnv.ResolveAsync(AiSlot.Assistant));

        var none = new AiClientFactory(NewDb(), HttpFactory(), Config());
        Assert.Null(await none.ResolveAsync(AiSlot.Analyst));
    }
}
