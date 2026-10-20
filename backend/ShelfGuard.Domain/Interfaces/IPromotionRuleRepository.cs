using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>A published rule joined with its published version (what the POS engine consumes).</summary>
public sealed record PublishedPromotionRuleRow(
    Guid RuleId,
    string Name,
    DateTime CreatedAt,
    Guid VersionId,
    string DefinitionJson);

/// <summary>
/// promotion_rules / promotion_rule_versions / promotion_rule_applications (RLS tenant-scoped).
/// Add* methods only stage changes: the sale path commits the application log together with the
/// sale in PosRepository's single SaveChanges (same scoped DbContext).
/// </summary>
public interface IPromotionRuleRepository
{
    /// <summary>Rules with Status = published and a PublishedVersionId, with that version's definition.</summary>
    Task<IReadOnlyList<PublishedPromotionRuleRow>> GetPublishedAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>All rules of the tenant (RLS-bound), newest first; optional status filter. Tracking off.</summary>
    Task<IReadOnlyList<PromotionRule>> ListAsync(string? status, CancellationToken ct = default);

    /// <summary>Versions by id (any rules) - used to resolve current/published definitions in bulk.</summary>
    Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Stages a hard delete of a superseded, never-published draft version (only UPDATE is blocked by the trigger).</summary>
    Task RemoveVersionAsync(Guid versionId, CancellationToken ct = default);

    Task<PromotionRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsAsync(Guid ruleId, CancellationToken ct = default);
    Task AddRuleAsync(PromotionRule rule, CancellationToken ct = default);
    Task AddVersionAsync(PromotionRuleVersion version, CancellationToken ct = default);

    Task AddApplicationsAsync(IEnumerable<PromotionRuleApplication> applications, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionRuleApplication>> GetApplicationsForTransactionAsync(
        Guid transactionId, CancellationToken ct = default);

    /// <summary>Runs <paramref name="work"/> (several SaveChanges) in one DB transaction: commit on success, roll back on any exception.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
