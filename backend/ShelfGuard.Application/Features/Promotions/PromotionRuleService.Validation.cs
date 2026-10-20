using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Promotions;

public sealed partial class PromotionRuleService
{
    /// <summary>
    /// Errors block publish, warnings do not. Codes and field paths are the Phase 2 contract
    /// (.claude/docs/promotion-rules.md); the web localizes by code.
    /// </summary>
    internal async Task<RuleValidationDto> ValidateDefinitionAsync(
        PromotionRuleCaller caller, HashSet<Guid>? scope, PromotionDefinition def, Guid? selfRuleId, CancellationToken ct)
    {
        var errors = new List<RuleIssueDto>();
        var warnings = new List<RuleIssueDto>();
        void Err(string code, string? field, string msg) => errors.Add(new RuleIssueDto(code, field, msg));
        void Warn(string code, string? field, string msg) => warnings.Add(new RuleIssueDto(code, field, msg));

        var p = def.Params;
        var now = DateTimeOffset.UtcNow;

        // ── type + params ──
        switch (def.Type)
        {
            case PromotionRuleTypes.PercentOff:
                if (p.Percent is null) Err("missing_param", "params.percent", "Percent is required.");
                else if (p.Percent <= 0m || p.Percent > 100m)
                    Err("percent_out_of_range", "params.percent", "Percent must be in (0, 100].");
                break;
            case PromotionRuleTypes.FixedOff:
                if (p.Amount is null) Err("missing_param", "params.amount", "Amount is required.");
                else if (p.Amount < PromotionEngine.MinPromoPrice)
                    Err("price_below_minimum", "params.amount", "Amount must be at least 0.01.");
                break;
            case PromotionRuleTypes.FixedPrice:
                if (p.Price is null) Err("missing_param", "params.price", "Price is required.");
                else if (p.Price < PromotionEngine.MinPromoPrice)
                    Err("price_below_minimum", "params.price", "Price must be at least 0.01.");
                break;
            case PromotionRuleTypes.BuyNGetM:
                if (p.BuyQty is null) Err("missing_param", "params.buyQty", "Buy quantity is required.");
                else if (p.BuyQty < 1) Err("invalid_quantity", "params.buyQty", "Buy quantity must be at least 1.");
                if (p.GetQty is null) Err("missing_param", "params.getQty", "Get quantity is required.");
                else if (p.GetQty < 1) Err("invalid_quantity", "params.getQty", "Get quantity must be at least 1.");
                if (p.GetPrice is null && p.GetPercent is null)
                    Err("missing_param", "params.getPrice", "Get price or get percent is required.");
                else if (p.GetPrice is { } gp && gp < PromotionEngine.MinPromoPrice)
                    Err("price_below_minimum", "params.getPrice", "Price must be at least 0.01.");
                else if (p.GetPrice is null && p.GetPercent is { } gpc && (gpc <= 0m || gpc > 100m))
                    Err("percent_out_of_range", "params.getPercent", "Percent must be in (0, 100].");
                if (p.MatchMode is not (null or PromotionMatchModes.PerItem or PromotionMatchModes.AcrossSet))
                    Err("missing_param", "params.matchMode", "Match mode must be per_item or across_set.");
                break;
            default:
                Err("missing_param", "type", "Rule type is required (percent_off, fixed_off, fixed_price, buy_n_get_m).");
                break;
        }
        if (def.Limits.MaxApplicationsPerReceipt is < 1)
            Err("invalid_quantity", "limits.maxApplicationsPerReceipt", "Limit must be at least 1.");

        // ── targets ──
        if (def.Targets.ItemIds.Count == 0 && def.Targets.CategoryIds.Count == 0)
            Err("no_targets", "targets", "Select at least one item or category.");

        var itemsById = await LoadItemsAsync(caller, def.Targets.ItemIds, ct);
        foreach (var id in def.Targets.ItemIds.Distinct().Where(i => !itemsById.ContainsKey(i)))
            Err("unknown_item", "targets.itemIds", $"Item {id} does not exist.");
        foreach (var id in def.Targets.CategoryIds.Distinct())
            if (!await _categories.ActiveExistsAsync(id, ct))
                Err("unknown_category", "targets.categoryIds", $"Category {id} does not exist.");

        // ── scope ──
        var s = def.Scope;
        if (s.ValidFrom is { } from && s.ValidTo is { } to && to < from)
            Err("invalid_period", "scope.validTo", "Period end is before its start.");
        if (s.Weekdays.Any(d => d is < 1 or > 7))
            Err("invalid_period", "scope.weekdays", "Weekdays must be 1 (Mon) .. 7 (Sun).");
        if (!string.IsNullOrWhiteSpace(s.TimeFrom) && PromotionDefinitionJson.ParseTime(s.TimeFrom) is null)
            Err("invalid_period", "scope.timeFrom", "Time must be HH:mm.");
        if (!string.IsNullOrWhiteSpace(s.TimeTo) && PromotionDefinitionJson.ParseTime(s.TimeTo) is null)
            Err("invalid_period", "scope.timeTo", "Time must be HH:mm.");

        if (s.StoreIds.Count > 0)
        {
            var known = (await _locations.GetAllAsync(ct)).Where(l => l.TenantId == caller.TenantId)
                .Select(l => l.Id).ToHashSet();
            foreach (var id in s.StoreIds.Distinct().Where(i => !known.Contains(i)))
                Err("unknown_store", "scope.storeIds", $"Store {id} does not exist.");
        }
        if (!InScope(scope, def))
            Err("store_scope_violation", "scope.storeIds", "Stores must be selected and inside your assigned stores.");

        // ── audience ──
        if (def.Audience.Kind is not (PromotionAudienceKinds.All or PromotionAudienceKinds.LoyaltyMember
                or PromotionAudienceKinds.LoyaltyTier))
            Err("missing_param", "audience.kind", "Audience must be all, loyalty_member or loyalty_tier.");
        if (def.Audience.Kind == PromotionAudienceKinds.LoyaltyTier)
        {
            if (def.Audience.TierIds.Count == 0)
                Err("tier_required", "audience.tierIds", "Select at least one loyalty tier.");
            else
            {
                var tiers = (await _loyalty.GetTierLadderAsync(caller.TenantId, ct)).Select(t => t.Id).ToHashSet();
                foreach (var id in def.Audience.TierIds.Distinct().Where(i => !tiers.Contains(i)))
                    Err("unknown_tier", "audience.tierIds", $"Tier {id} does not exist.");
            }
        }

        // ── warnings ──
        if (s.StoreIds.Count == 0)
            Warn("all_stores", "scope.storeIds", "The rule applies to every store.");
        if (s.ValidTo is { } end && end <= now)
            Warn("already_expired", "scope.validTo", "The rule period has already ended.");

        foreach (var item in itemsById.Values.Where(i => def.Targets.ItemIds.Contains(i.Id)))
        {
            if (item.PriceRetail is not { } regular) continue;
            var promo = def.Type switch
            {
                PromotionRuleTypes.FixedPrice => p.Price,
                PromotionRuleTypes.BuyNGetM => p.GetPrice,
                _ => null,
            };
            if (promo is { } pp && pp >= regular)
                Warn("promo_price_not_lower", "params.price",
                    $"Promo price {pp:0.00} is not lower than the regular price {regular:0.00} of '{item.Name}'.");
        }

        await AddOverlapWarningsAsync(caller.TenantId, def, selfRuleId, Warn, ct);
        return new RuleValidationDto(errors, warnings);
    }

    /// <summary>Another published/paused rule sharing an item or category with overlapping stores + period.</summary>
    private async Task AddOverlapWarningsAsync(
        Guid tenantId, PromotionDefinition def, Guid? selfRuleId, Action<string, string?, string> warn, CancellationToken ct)
    {
        var others = (await _rules.ListAsync(null, ct))
            .Where(r => r.TenantId == tenantId && r.Id != selfRuleId && r.PublishedVersionId is not null
                        && r.Status is PromotionRule.StatusPublished or PromotionRule.StatusPaused)
            .ToList();
        if (others.Count == 0) return;

        var versions = (await _rules.GetVersionsByIdsAsync(others.Select(r => r.PublishedVersionId!.Value), ct))
            .ToDictionary(v => v.Id);
        foreach (var o in others)
        {
            if (!versions.TryGetValue(o.PublishedVersionId!.Value, out var v)) continue;
            var other = PromotionDefinitionJson.TryParse(v.DefinitionJson);
            if (other is null) continue;

            var sharesTarget = other.Targets.ItemIds.Intersect(def.Targets.ItemIds).Any()
                               || other.Targets.CategoryIds.Intersect(def.Targets.CategoryIds).Any();
            if (!sharesTarget) continue;
            if (!StoresOverlap(def.Scope, other.Scope) || !PeriodsOverlap(def.Scope, other.Scope)) continue;
            if (def.Scope.Weekdays.Count > 0 && other.Scope.Weekdays.Count > 0
                && !def.Scope.Weekdays.Intersect(other.Scope.Weekdays).Any()) continue;

            warn("overlaps_other_rule", null,
                $"Overlaps with rule '{o.Name}'; the best price for the customer wins.");
        }
    }

    private static bool StoresOverlap(PromotionScope a, PromotionScope b) =>
        a.StoreIds.Count == 0 || b.StoreIds.Count == 0 || a.StoreIds.Intersect(b.StoreIds).Any();

    private static bool PeriodsOverlap(PromotionScope a, PromotionScope b)
    {
        var aFrom = a.ValidFrom ?? DateTimeOffset.MinValue;
        var aTo = a.ValidTo ?? DateTimeOffset.MaxValue;
        var bFrom = b.ValidFrom ?? DateTimeOffset.MinValue;
        var bTo = b.ValidTo ?? DateTimeOffset.MaxValue;
        return aFrom < bTo && bFrom < aTo;
    }
}
