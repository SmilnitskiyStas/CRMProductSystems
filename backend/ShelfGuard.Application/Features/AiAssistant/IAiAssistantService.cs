namespace ShelfGuard.Application.Features.AiAssistant;

/// <summary>
/// Application-layer service for the AI Business Assistant.
/// Validates input, checks configuration, delegates to the Infrastructure advisor.
/// </summary>
public interface IAiAssistantService
{
    Task<(BusinessAssistantResponse? Response, string? Error)> AskAsync(
        Guid tenantId,
        BusinessAssistantRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Infrastructure contract for the isolated Claude AI client.
/// Aggregates cross-module DB context and calls Claude API.
/// Implemented in ShelfGuard.Infrastructure/AI/BusinessAssistant/BusinessAssistantAdvisor.cs
/// </summary>
public interface IBusinessAssistantAdvisor
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches cross-module context for the given tenant, builds a Claude prompt,
    /// and returns the AI reply with a context summary.
    /// </summary>
    Task<BusinessAssistantResult> AdviseAsync(
        Guid tenantId,
        string message,
        CancellationToken ct = default);
}
