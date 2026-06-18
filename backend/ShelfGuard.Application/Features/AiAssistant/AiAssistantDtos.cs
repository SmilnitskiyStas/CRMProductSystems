namespace ShelfGuard.Application.Features.AiAssistant;

// ── Request / Response ────────────────────────────────────────────────────────

/// <summary>POST /api/ai/assistant request body.</summary>
public sealed record BusinessAssistantRequest(string Message);

/// <summary>POST /api/ai/assistant response.</summary>
public sealed record BusinessAssistantResponse(
    string Reply,
    BusinessAssistantContextSummary Context,
    string AiModel,
    int TokensUsed);

/// <summary>Summary of what data was included in the context passed to Claude.</summary>
public sealed record BusinessAssistantContextSummary(
    int CriticalStockBatchesCount,
    int PendingOrdersCount,
    int SalesDaysCount,
    int ActiveSuppliersCount);

// ── Result from advisor (returned from Infrastructure to Application) ──────────

public sealed record BusinessAssistantResult(
    string Reply,
    BusinessAssistantContextSummary ContextSummary,
    string Model,
    int TokensUsed);
