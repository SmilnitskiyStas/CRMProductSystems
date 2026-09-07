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
/// Managed-AI Phase 1 — provider-only per-tenant AI config service.
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

    [Fact]
    public async Task Get_NoRow_ReturnsNotConfigured()
    {
        _integrations.GetByServiceAsync(Tenant, "claude", Arg.Any<CancellationToken>())
            .Returns(((IntegrationConfigDto?)null, (string?)null));

        var dto = await _sut.GetAsync(Tenant);

        Assert.False(dto.IsConfigured);
        Assert.Null(dto.ApiKeyLast4);
    }

    [Fact]
    public async Task Get_MaskedRow_ReshapesLast4AndFields()
    {
        var cfg = new JsonObject
        {
            ["api_key"] = "••••cdef",
            ["model"] = "claude-opus-4-1",
            ["extra_instructions"] = "Це аптека.",
        };
        _integrations.GetByServiceAsync(Tenant, "claude", Arg.Any<CancellationToken>())
            .Returns((new IntegrationConfigDto(Guid.NewGuid(), "claude", cfg, true, DateTime.UtcNow), (string?)null));

        var dto = await _sut.GetAsync(Tenant);

        Assert.True(dto.IsConfigured);
        Assert.True(dto.IsEnabled);
        Assert.Equal("cdef", dto.ApiKeyLast4);
        Assert.Equal("claude-opus-4-1", dto.Model);
        Assert.Equal("Це аптека.", dto.ExtraInstructions);
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
    }

    [Fact]
    public async Task Update_RealApiKey_WritesItVerbatimAndOmitsBlankExtra()
    {
        UpsertIntegrationRequest? captured = null;
        _integrations.UpsertAsync(Tenant, "claude", Arg.Do<UpsertIntegrationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.UpdateAsync(Tenant, new UpdateAiAgentRequest(ApiKey: "sk-ant-real", Model: null, ExtraInstructions: "   ", IsEnabled: false));

        Assert.Equal("sk-ant-real", (string?)captured!.Config["api_key"]);
        Assert.Equal("claude-sonnet-4-6", (string?)captured.Config["model"]); // default filled in
        Assert.False(captured.Config.ContainsKey("extra_instructions"));
        Assert.False(captured.IsEnabled);
    }

    [Fact]
    public async Task Test_NoKeyAnywhere_ReturnsOkFalse_WithoutProbing()
    {
        _repo.GetByServiceAsync(Tenant, "claude", Arg.Any<CancellationToken>())
            .Returns((IntegrationConfig?)null);

        var result = await _sut.TestAsync(Tenant, candidate: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        await _tester.DidNotReceiveWithAnyArgs().ProbeAsync(default!, default!, default);
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
        _tester.ProbeAsync("sk-ant-stored", "claude-sonnet-4-6", Arg.Any<CancellationToken>())
            .Returns(new AiProbeResult(true, null));

        var result = await _sut.TestAsync(Tenant, new UpdateAiAgentRequest(ApiKey: "••••stored", Model: null, ExtraInstructions: null));

        Assert.True(result.Ok);
        await _tester.Received(1).ProbeAsync("sk-ant-stored", "claude-sonnet-4-6", Arg.Any<CancellationToken>());
    }
}
