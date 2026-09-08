using System.Text.Json.Nodes;
using NSubstitute;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Provider;

/// <summary>
/// Managed-AI Phase 1 (provider-only per-tenant AI config) + Phase 2 (Claude / OpenAI choice).
/// </summary>
public sealed class TenantAiConfigServiceTests
{
    private readonly IIntegrationService _integrations = Substitute.For<IIntegrationService>();
    private readonly IIntegrationRepository _repo = Substitute.For<IIntegrationRepository>();
    private readonly IAiConnectivityTester _tester = Substitute.For<IAiConnectivityTester>();
    private readonly TenantAiConfigService _sut;

    private static readonly Guid Tenant = Guid.NewGuid();

    public TenantAiConfigServiceTests() =>
        _sut = new TenantAiConfigService(_integrations, _repo, _tester);

    private void StubRow(string service, JsonObject? config, bool enabled = true) =>
        _integrations.GetByServiceAsync(Tenant, service, Arg.Any<CancellationToken>())
            .Returns((config is null ? null : new IntegrationConfigDto(Guid.NewGuid(), service, config, enabled, DateTime.UtcNow),
                      (string?)null));

    [Fact]
    public async Task Get_NoRow_ReturnsNotConfigured()
    {
        StubRow("claude", null);
        StubRow("openai", null);

        var dto = await _sut.GetAsync(Tenant);

        Assert.False(dto.IsConfigured);
        Assert.Null(dto.Provider);
        Assert.Null(dto.ApiKeyLast4);
    }

    [Fact]
    public async Task Get_ClaudeRow_ReshapesLast4AndFields()
    {
        StubRow("openai", null);
        StubRow("claude", new JsonObject
        {
            ["api_key"] = "••••cdef",
            ["model"] = "claude-opus-4-1",
            ["extra_instructions"] = "Це аптека.",
        });

        var dto = await _sut.GetAsync(Tenant);

        Assert.True(dto.IsConfigured);
        Assert.Equal("claude", dto.Provider);
        Assert.Equal("cdef", dto.ApiKeyLast4);
        Assert.Equal("claude-opus-4-1", dto.Model);
        Assert.Equal("Це аптека.", dto.ExtraInstructions);
    }

    [Fact]
    public async Task Get_RowWithoutKey_StillReturnsTheSavedConfig()
    {
        // Regression: a preset (extra_instructions) saved without a per-tenant key used to
        // come back as IsConfigured=false with everything null — the preset "vanished".
        StubRow("openai", null);
        StubRow("claude", new JsonObject
        {
            ["model"] = "claude-sonnet-4-6",
            ["extra_instructions"] = "Це квітковий магазин.",
        });

        var dto = await _sut.GetAsync(Tenant);

        Assert.True(dto.IsConfigured);
        Assert.Equal("claude", dto.Provider);
        Assert.Null(dto.ApiKeyLast4);
        Assert.Equal("claude-sonnet-4-6", dto.Model);
        Assert.Equal("Це квітковий магазин.", dto.ExtraInstructions);
    }

    [Fact]
    public async Task Get_PrefersTheOpenAiRow()
    {
        StubRow("openai", new JsonObject { ["api_key"] = "••••1234", ["model"] = "gpt-4o", ["base_url"] = "https://proxy/v1" });
        StubRow("claude", new JsonObject { ["api_key"] = "••••abcd", ["model"] = "claude-sonnet-4-6" });

        var dto = await _sut.GetAsync(Tenant);

        Assert.Equal("openai", dto.Provider);
        Assert.Equal("1234", dto.ApiKeyLast4);
        Assert.Equal("https://proxy/v1", dto.BaseUrl);
    }

    [Fact]
    public async Task Update_BlankApiKey_SendsMaskedPlaceholderToKeepStoredKey()
    {
        UpsertIntegrationRequest? captured = null;
        _integrations.UpsertAsync(Tenant, "claude", Arg.Do<UpsertIntegrationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.UpdateAsync(Tenant, new UpdateAiAgentRequest(ApiKey: "  ", Model: "claude-sonnet-4-6", ExtraInstructions: null, IsEnabled: true));

        Assert.NotNull(captured);
        Assert.Equal("••••", (string?)captured!.Config["api_key"]);
        Assert.Equal("claude-sonnet-4-6", (string?)captured.Config["model"]);
        Assert.True(captured.IsEnabled);
        // switching to (or staying on) claude removes any openai row
        await _integrations.Received(1).DeleteAsync(Tenant, "openai", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_OpenAiProvider_WritesOpenAiRow_DropsClaude_AndKeepsBaseUrl()
    {
        UpsertIntegrationRequest? captured = null;
        _integrations.UpsertAsync(Tenant, "openai", Arg.Do<UpsertIntegrationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.UpdateAsync(Tenant, new UpdateAiAgentRequest(
            ApiKey: "sk-openai-real", Model: null, ExtraInstructions: null, IsEnabled: true,
            Provider: "openai", BaseUrl: "https://azure.example/v1"));

        Assert.Equal("sk-openai-real", (string?)captured!.Config["api_key"]);
        Assert.Equal("gpt-4o-mini", (string?)captured.Config["model"]); // openai default
        Assert.Equal("https://azure.example/v1", (string?)captured.Config["base_url"]);
        await _integrations.Received(1).DeleteAsync(Tenant, "claude", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_NoKeyAnywhere_ReturnsOkFalse_WithoutProbing()
    {
        _repo.GetByServiceAsync(Tenant, "claude", Arg.Any<CancellationToken>()).Returns((IntegrationConfig?)null);

        var result = await _sut.TestAsync(Tenant, candidate: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        await _tester.DidNotReceiveWithAnyArgs().ProbeAsync(default!, default);
    }

    [Fact]
    public async Task Test_OpenAiCandidate_ProbesWithTheRightProviderAndBaseUrl()
    {
        _repo.GetByServiceAsync(Tenant, "openai", Arg.Any<CancellationToken>()).Returns((IntegrationConfig?)null);
        _tester.ProbeAsync(Arg.Any<AiProviderConfig>(), Arg.Any<CancellationToken>())
            .Returns(new AiProbeResult(true, null));

        var result = await _sut.TestAsync(Tenant, new UpdateAiAgentRequest(
            ApiKey: "sk-oai", Model: "gpt-4o", ExtraInstructions: null, IsEnabled: true,
            Provider: "openai", BaseUrl: "https://proxy/v1"));

        Assert.True(result.Ok);
        await _tester.Received(1).ProbeAsync(
            Arg.Is<AiProviderConfig>(c => c.Provider == "openai" && c.ApiKey == "sk-oai" && c.Model == "gpt-4o" && c.BaseUrl == "https://proxy/v1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_UsesStoredKeyWhenCandidateKeyIsMasked()
    {
        _repo.GetByServiceAsync(Tenant, "claude", Arg.Any<CancellationToken>())
            .Returns(new IntegrationConfig
            {
                TenantId = Tenant,
                Service = "claude",
                Config = "{\"api_key\":\"sk-ant-stored\",\"model\":\"claude-sonnet-4-6\"}",
            });
        _tester.ProbeAsync(Arg.Any<AiProviderConfig>(), Arg.Any<CancellationToken>())
            .Returns(new AiProbeResult(true, null));

        var result = await _sut.TestAsync(Tenant, new UpdateAiAgentRequest(ApiKey: "••••stored", Model: null, ExtraInstructions: null));

        Assert.True(result.Ok);
        await _tester.Received(1).ProbeAsync(
            Arg.Is<AiProviderConfig>(c => c.Provider == "claude" && c.ApiKey == "sk-ant-stored" && c.Model == "claude-sonnet-4-6"),
            Arg.Any<CancellationToken>());
    }
}
