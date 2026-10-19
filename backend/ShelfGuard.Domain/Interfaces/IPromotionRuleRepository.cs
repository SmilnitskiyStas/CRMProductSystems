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

    Task<PromotionRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsAsync(Guid ruleId, CancellationToken ct = default);
    Task AddRuleAsync(PromotionRule rule, CancellationToken ct = default);
    Task AddVersionAsync(PromotionRuleVersion version, CancellationToken ct = default);

    Task AddApplicationsAsync(IEnumerable<PromotionRuleApplication> applications, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionRuleApplication>> GetApplicationsForTransactionAsync(
        Guid transactionId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
