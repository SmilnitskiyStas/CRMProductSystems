using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Features.ConsumerAssistant;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.ConsumerAssistant;

/// <summary>
/// managed-AI Phase 4b — the consumer-facing AI shopping assistant. The tenant-scope override
/// is mocked as a pure pass-through (real RLS behaviour is a live integration concern); this
/// exercises the config gate, the consumer guardrail wiring, and the audit write.
/// </summary>
public sealed class ConsumerAssistantServiceTests
{
    private readonly ITenantSessionOverride _scope = Substitute.For<ITenantSessionOverride>();
    private readonly IIntegrationRepository _integrations = Substitute.For<IIntegrationRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ILoyaltyService _loyalty = Substitute.For<ILoyaltyService>();
    private readonly IConsumerContentService _content = Substitute.For<IConsumerContentService>();
    private readonly IAiClientFactory _ai = Substitute.For<IAiClientFactory>();
    private readonly IAiPromptResolver _prompt = Substitute.For<IAiPromptResolver>();
    private readonly IConsumerAiRequestRepository _audit = Substitute.For<IConsumerAiRequestRepository>();
    private readonly ConsumerAssistantService _sut;

    private static readonly Guid Consumer = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public ConsumerAssistantServiceTests()
    {
        _sut = new ConsumerAssistantService(_scope, _integrations, _tenants, _loyalty, _content, _ai, _prompt, _audit);

        // pure pass-through for both closed generics the service uses
        _scope.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<string?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<string?>>>()());
        _scope.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<int>>>()());

        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Tenant.Create("Свіжий Кут", "svizhyi-kut"));

        _loyalty.GetMembershipsForConsumerAsync(Consumer, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LoyaltyMembershipSummaryDto>());
        _loyalty.GetNetworksForConsumerAsync(Consumer, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LoyaltyNetworkSummaryDto>());
        _loyalty.GetTierProgressAsync(Consumer, TenantId, Arg.Any<CancellationToken>())
            .Returns((null, null, (int?)null));
        _loyalty.GetTierLadderForConsumerAsync(Consumer, TenantId, Arg.Any<CancellationToken>())
            .Returns((null, null, (int?)null));
        _loyalty.GetHistoryAsync(Consumer, TenantId, 1, 10, Arg.Any<CancellationToken>())
            .Returns((null, null, (int?)null));
    }

    private void StubRow(string? config, bool enabled = true) =>
        _integrations.GetByServiceAsync(TenantId, "ai_consumer", Arg.Any<CancellationToken>())
            .Returns(config is null ? null : new IntegrationConfig { TenantId = TenantId, Service = "ai_consumer", Config = config, IsEnabled = enabled });

    [Fact]
    public async Task Ask_EmptyMessage_Returns400()
    {
        var (answer, error, status) = await _sut.AskAsync(Consumer, TenantId, "   ");
        Assert.Null(answer);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Ask_TenantNotFound_Returns404()
    {
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var (_, _, status) = await _sut.AskAsync(Consumer, TenantId, "скільки в мене бонусів?");
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Ask_NoAiConsumerRow_Returns404()
    {
        StubRow(null);
        var (answer, _, status) = await _sut.AskAsync(Consumer, TenantId, "привіт");
        Assert.Null(answer);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Ask_DisabledRow_Returns404()
    {
        StubRow("""{"provider":"claude","api_key":"sk-ant"}""", enabled: false);
        var (_, _, status) = await _sut.AskAsync(Consumer, TenantId, "привіт");
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Ask_NoKeyAndNoEnv_Returns404()
    {
        StubRow("""{"provider":"claude","model":"claude-x"}""");
        _ai.CreateOrEnv("claude", null, "claude-x", null).Returns((IAiChatClient?)null);

        var (_, _, status) = await _sut.AskAsync(Consumer, TenantId, "привіт");
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Ask_Configured_UsesConsumerGuardrail_CallsModel_AndWritesAudit()
    {
        StubRow("""{"provider":"claude","api_key":"sk-ant","model":"claude-x","extra_instructions":"це аптека"}""");
        var client = Substitute.For<IAiChatClient>();
        client.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AiChatResult("У вас 120 бонусів.", "claude-x", 55));
        _ai.CreateOrEnv("claude", "sk-ant", "claude-x", null).Returns(client);
        _prompt.Compose(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), AiSlot.Consumer).Returns("GUARDRAIL");

        var (answer, error, status) = await _sut.AskAsync(Consumer, TenantId, "скільки в мене бонусів?");

        Assert.Null(error);
        Assert.NotNull(answer);
        Assert.Equal("У вас 120 бонусів.", answer!.Text);
        Assert.Equal(55, answer.TokensUsed);

        _prompt.Received(1).Compose("Свіжий Кут", "це аптека", Arg.Any<string>(), AiSlot.Consumer);
        await client.Received(1).CompleteAsync(
            Arg.Is<AiChatRequest>(r => r.SystemPrompt == "GUARDRAIL" && r.UserPrompt.Contains("скільки в мене бонусів?")),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).AddAsync(
            Arg.Is<ConsumerAiRequest>(e =>
                e.TenantId == TenantId &&
                e.ConsumerAccountId == Consumer &&
                e.PromptExcerpt == "скільки в мене бонусів?" &&
                e.ResponseExcerpt == "У вас 120 бонусів." &&
                e.Model == "claude-x" && e.TokensUsed == 55),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ask_ModelThrows_Returns502_NoAudit()
    {
        StubRow("""{"provider":"claude","api_key":"sk-ant","model":"claude-x"}""");
        var client = Substitute.For<IAiChatClient>();
        client.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("credit balance too low"));
        _ai.CreateOrEnv("claude", "sk-ant", "claude-x", null).Returns(client);
        _prompt.Compose(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), AiSlot.Consumer).Returns("G");

        var (answer, _, status) = await _sut.AskAsync(Consumer, TenantId, "привіт");

        Assert.Null(answer);
        Assert.Equal(502, status);
        await _audit.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
