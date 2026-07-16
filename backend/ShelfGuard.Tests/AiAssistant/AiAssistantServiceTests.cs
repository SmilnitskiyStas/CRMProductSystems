using NSubstitute;
using ShelfGuard.Application.Features.AiAssistant;
using Xunit;

namespace ShelfGuard.Tests.AiAssistant;

/// <summary>
/// Block 7 pre-launch audit (AI Orders / AI Assistant). Mirrors AiOrderServiceTests' error
/// handling coverage: a Claude API failure must degrade to a readable error, never throw
/// and never 500 the whole /api/ai/assistant endpoint.
/// </summary>
public sealed class AiAssistantServiceTests
{
    private readonly IBusinessAssistantAdvisor _advisor = Substitute.For<IBusinessAssistantAdvisor>();
    private readonly AiAssistantService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AiAssistantServiceTests() => _sut = new AiAssistantService(_advisor);

    [Fact]
    public async Task AskAsync_EmptyMessage_ReturnsError_DoesNotCallAdvisor()
    {
        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest(""));

        Assert.Null(response);
        Assert.NotNull(error);
        await _advisor.DidNotReceive().IsConfiguredAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_WhitespaceMessage_ReturnsError()
    {
        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest("   "));

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task AskAsync_ApiKeyNotConfigured_ReturnsReadableError_DoesNotCallAdvise()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest("Що з залишками?"));

        Assert.Null(response);
        Assert.Contains("Claude API", error);
        await _advisor.DidNotReceive().AdviseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_AdvisorThrows_ReturnsError_DoesNotThrow()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _advisor.AdviseAsync(_tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BusinessAssistantResult>(_ => throw new InvalidOperationException("Anthropic API error 529"));

        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest("Що з залишками?"));

        Assert.Null(response);
        Assert.NotNull(error);
        Assert.Contains("AI сервіс недоступний", error);
    }

    [Fact]
    public async Task AskAsync_AdvisorThrowsCreditBalanceError_ReturnsUkrainianBillingMessage()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _advisor.AdviseAsync(_tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BusinessAssistantResult>(_ => throw new InvalidOperationException(
                "Your credit balance is too low to access the Anthropic API"));

        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest("Що з залишками?"));

        Assert.Null(response);
        Assert.Contains("кредитів", error);
    }

    [Fact]
    public async Task AskAsync_Success_ReturnsReplyAndContextSummary()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _advisor.AdviseAsync(_tenantId, "Що з залишками?", Arg.Any<CancellationToken>())
            .Returns(new BusinessAssistantResult(
                "Критичних залишків немає.",
                new BusinessAssistantContextSummary(0, 2, 15, 5),
                "claude-sonnet-4-6",
                412));

        var (response, error) = await _sut.AskAsync(_tenantId, new BusinessAssistantRequest("Що з залишками?"));

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("Критичних залишків немає.", response!.Reply);
        Assert.Equal(2, response.Context.PendingOrdersCount);
        Assert.Equal(412, response.TokensUsed);
    }
}
