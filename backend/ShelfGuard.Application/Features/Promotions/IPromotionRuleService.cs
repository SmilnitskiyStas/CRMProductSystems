namespace ShelfGuard.Application.Features.Promotions;

/// <summary>Manager-side CRUD, lifecycle, validation and simulation of promotion rules (Phase 2).</summary>
public interface IPromotionRuleService
{
    Task<IReadOnlyList<RuleListItemDto>> ListAsync(PromotionRuleCaller caller, string? status, Guid? storeId, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> GetAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> CreateAsync(PromotionRuleCaller caller, CreatePromotionRuleRequest request, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> UpdateAsync(PromotionRuleCaller caller, Guid id, UpdatePromotionRuleRequest request, CancellationToken ct = default);
    Task<PromotionOutcome<RuleValidationDto>> ValidateAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> PublishAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> PauseAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> ResumeAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<RuleDetailDto>> ArchiveAsync(PromotionRuleCaller caller, Guid id, CancellationToken ct = default);
    Task<PromotionOutcome<SimulateResultDto>> SimulateAsync(PromotionRuleCaller caller, SimulateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TierLookupDto>> LookupTiersAsync(PromotionRuleCaller caller, CancellationToken ct = default);
    Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(PromotionRuleCaller caller, string? search, Guid? categoryId, int? limit, CancellationToken ct = default, IReadOnlyList<Guid>? ids = null);
}
