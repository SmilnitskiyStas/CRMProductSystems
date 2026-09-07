namespace ShelfGuard.Application.Services;

/// <summary>
/// A one-shot, side-effect-free connectivity probe for an AI provider's credentials — used by
/// the provider's "Перевірити підключення" button (<c>POST /api/provider/tenants/{id}/ai-agent/test</c>).
/// Isolated behind this interface so the Application layer never references a model SDK
/// (ADR-015 AI-isolation); the implementation lives in <c>ShelfGuard.Infrastructure.AI</c>.
/// Never throws — failures come back as <see cref="AiProbeResult.Error"/>.
/// </summary>
public interface IAiConnectivityTester
{
    Task<AiProbeResult> ProbeAsync(string apiKey, string model, CancellationToken ct = default);
}

public sealed record AiProbeResult(bool Ok, string? Error);
