namespace ShelfGuard.Application.Features.AiAssistant;

/// <summary>
/// Orchestrates the AI Business Assistant pipeline (TASK-250, v4 Phase 6).
/// Delegates context aggregation and Claude API call to the isolated
/// <see cref="IBusinessAssistantAdvisor"/> implementation in Infrastructure/AI.
/// Business logic rule: thin Application service, AI isolation in Infrastructure.
/// </summary>
public sealed class AiAssistantService : IAiAssistantService
{
    private readonly IBusinessAssistantAdvisor _advisor;

    public AiAssistantService(IBusinessAssistantAdvisor advisor)
    {
        _advisor = advisor;
    }

    public async Task<(BusinessAssistantResponse? Response, string? Error)> AskAsync(
        Guid tenantId,
        BusinessAssistantRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
            return (null, "Message cannot be empty.");

        if (!await _advisor.IsConfiguredAsync(ct))
            return (null, "AI-агент не налаштований. Зверніться до вашого провайдера, щоб підключити його.");

        BusinessAssistantResult result;
        try
        {
            result = await _advisor.AdviseAsync(tenantId, request.Message, ct);
        }
        catch (ShelfGuard.Application.Services.WorkAccessDeniedException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, "AI сервіс тимчасово недоступний. Повторіть спробу пізніше.");
        }

        return (new BusinessAssistantResponse(
            result.Reply,
            result.ContextSummary,
            result.Model,
            result.TokensUsed), null);
    }
}
