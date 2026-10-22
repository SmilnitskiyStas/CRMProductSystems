using ShelfGuard.Application.Features.Receipts.Dtos;

namespace ShelfGuard.Application.Features.Receipts;

/// <summary>
/// TASK-755: runs <see cref="IAutoReplenishmentService"/> for exactly ONE tenant from the daily
/// worker cron (no staff JWT). Same isolation contract as the production-demand runner
/// (TASK-754): one DB transaction whose RLS context is bound to <c>tenantId</c> only
/// (SET LOCAL app.tenant_id + tenant-wide non-bypass store scope) - never worker/provider bypass.
/// </summary>
public interface IAutoReplenishmentTenantRunner
{
    Task<AutoReplenishmentRunResult> RunAsync(Guid tenantId, CancellationToken ct = default);
}

public enum AutoReplenishmentRunStatus
{
    Completed,
    TenantNotFound,
    /// <summary>Tenant inactive or without an active non-production location.</summary>
    Skipped,
}

public sealed record AutoReplenishmentRunResult(
    AutoReplenishmentRunStatus Status,
    List<ReceiptDto> Drafts,
    string? Reason = null);
