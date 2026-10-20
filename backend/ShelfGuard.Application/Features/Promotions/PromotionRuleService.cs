using System.Text.Json;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Promotions;

/// <summary>
/// Promotion-rule management (Phase 2). Versions are immutable (DB trigger blocks UPDATE):
///  * saving while the current version is NOT the published one replaces the draft - a new version
///    the superseded unpublished draft row is DELETED (delete is allowed, only UPDATE is blocked)
///    and the replacement is inserted with the SAME VersionNumber (autosave keeps "v1 draft");
///  * saving while the current version IS the published one just adds N+1 (published stays live);
///  * a new version after a published one gets max + 1; numbers are unique per rule.
/// Store scoping mirrors RegisterService (store_manager / network_manager: assigned stores only).
/// </summary>
public sealed partial class PromotionRuleService : IPromotionRuleService
{
    public const int MaxLookupLimit = 50;
    public const int MaxSimulateLines = 200;

    private readonly IPromotionRuleRepository _rules;
    private readonly IItemRepository _items;
    private readonly ICategoryRepository _categories;
    private readonly ILocationRepository _locations;
    private readonly ILoyaltyRepository _loyalty;
    private readonly IUserRepository _users;
    private readonly IUserLocationRepository _userLocations;

    public PromotionRuleService(
        IPromotionRuleRepository rules,
        IItemRepository items,
        ICategoryRepository categories,
        ILocationRepository locations,
        ILoyaltyRepository loyalty,
        IUserRepository users,
        IUserLocationRepository userLocations)
    {
        _rules = rules;
        _items = items;
        _categories = categories;
        _locations = locations;
        _loyalty = loyalty;
        _users = users;
        _userLocations = userLocations;
    }

    // ── queries ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RuleListItemDto>> ListAsync(
        PromotionRuleCaller caller, string? status, Guid? storeId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(caller, ct);
        var rules = await _rules.ListAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant(), ct);
        var versions = (await _rules.GetVersionsByIdsAsync(
                rules.SelectMany(r => new[] { r.CurrentVersionId, r.PublishedVersionId })
                    .Where(i => i.HasValue).Select(i => i!.Value), ct))
            .ToDictionary(v => v.Id);

        var result = new List<RuleListItemDto>();
        foreach (var r in rules.Where(r => r.TenantId == caller.TenantId))
        {
            var cur = r.CurrentVersionId is { } cid && versions.TryGetValue(cid, out var cv) ? cv : null;
            var pub = r.PublishedVersionId is { } pid && versions.TryGetValue(pid, out var pv) ? pv : null;
            var def = PromotionDefinitionJson.TryParse(cur?.DefinitionJson);
            if (!VisibleTo(scope, def, PromotionDefinitionJson.TryParse(pub?.DefinitionJson))) continue;
            if (storeId is { } sid && def is not null
                && def.Scope.StoreIds.Count > 0 && !def.Scope.StoreIds.Contains(sid)) continue;

            result.Add(new RuleListItemDto(
                r.Id, r.Name, r.Description, r.Status, def?.Type,
                cur?.VersionNumber ?? 0, pub?.VersionNumber,
                def?.Scope.StoreIds ?? [], def?.Scope.ValidFrom, def?.Scope.ValidTo,
                r.CurrentVersionId != r.PublishedVersionId, r.UpdatedAt));
        }
        return result;
    }

    public async Task<PromotionOutcome<RuleDetailDto>> GetAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default)
    {
        var (rule, versions, _) = await LoadAsync(caller, id, ct);
        return rule is null
            ? NotFound<RuleDetailDto>()
            : PromotionOutcome<RuleDetailDto>.Success(await BuildDetailAsync(rule, versions, ct));
    }

    // ── create / update ────────────────────────────────────────────────────

    public async Task<PromotionOutcome<RuleDetailDto>> CreateAsync(
        PromotionRuleCaller caller, CreatePromotionRuleRequest request, CancellationToken ct = default)
    {
        var prep = await PrepareSaveAsync(caller, request.Name, request.Definition, ct);
        if (prep.Failed) return prep.Fail<RuleDetailDto>();

        var rule = new PromotionRule
        {
            TenantId = caller.TenantId,
            Name = prep.Name!,
            Description = Clean(request.Description),
            CreatedBy = caller.UserId,
        };
        // Circular FK (rule.CurrentVersionId <-> version.RuleId): rule, then version, then the pointer
        // - all in one transaction so a failure leaves nothing behind.
        await _rules.ExecuteInTransactionAsync(async () =>
        {
            await _rules.AddRuleAsync(rule, ct);
            await _rules.SaveChangesAsync(ct);
            var version = PromotionRuleVersion.Create(caller.TenantId, rule.Id, 1, prep.Json!, caller.UserId);
            await _rules.AddVersionAsync(version, ct);
            await _rules.SaveChangesAsync(ct);
            rule.CurrentVersionId = version.Id;
            rule.UpdatedAt = DateTime.UtcNow;
            await _rules.SaveChangesAsync(ct);
            return true;
        }, ct);

        return PromotionOutcome<RuleDetailDto>.Success(
            await BuildDetailAsync(rule, await _rules.GetVersionsAsync(rule.Id, ct), ct), 201);
    }

    public async Task<PromotionOutcome<RuleDetailDto>> UpdateAsync(
        PromotionRuleCaller caller, Guid id, UpdatePromotionRuleRequest request, CancellationToken ct = default)
    {
        var (rule, versions, _) = await LoadAsync(caller, id, ct);
        if (rule is null) return NotFound<RuleDetailDto>();
        if (rule.Status == PromotionRule.StatusArchived)
            return PromotionOutcome<RuleDetailDto>.Fail(400, "Archived rules cannot be edited.");

        var prep = await PrepareSaveAsync(caller, request.Name, request.Definition, ct);
        if (prep.Failed) return prep.Fail<RuleDetailDto>();

        var superseded = rule.CurrentVersionId;
        var replacesDraft = superseded is { } cur && cur != rule.PublishedVersionId
                            && versions.Any(v => v.Id == cur);
        // A draft replacing a draft keeps its number (autosave stays "v1 draft"); otherwise max + 1.
        var number = replacesDraft
            ? versions.First(v => v.Id == superseded).VersionNumber
            : (versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1);

        await _rules.ExecuteInTransactionAsync(async () =>
        {
        if (replacesDraft)
        {
            // Free the (RuleId, VersionNumber) slot: unpoint the rule, delete the old draft (the
            // trigger only blocks UPDATE), then insert the replacement and repoint.
            rule.CurrentVersionId = rule.PublishedVersionId;
            await _rules.SaveChangesAsync(ct);
            await _rules.RemoveVersionAsync(superseded!.Value, ct);
            await _rules.SaveChangesAsync(ct);
        }

        var version = PromotionRuleVersion.Create(
            caller.TenantId, rule.Id, number, prep.Json!, caller.UserId, Clean(request.ChangeComment));
        await _rules.AddVersionAsync(version, ct);
        await _rules.SaveChangesAsync(ct);

        rule.CurrentVersionId = version.Id;
        rule.Name = prep.Name!;
        rule.Description = Clean(request.Description);
        rule.UpdatedAt = DateTime.UtcNow;
        await _rules.SaveChangesAsync(ct);
        return true;
        }, ct);

        return PromotionOutcome<RuleDetailDto>.Success(
            await BuildDetailAsync(rule, await _rules.GetVersionsAsync(rule.Id, ct), ct));
    }

    // ── lifecycle ──────────────────────────────────────────────────────────

    public async Task<PromotionOutcome<RuleValidationDto>> ValidateAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default)
    {
        var (rule, versions, scope) = await LoadAsync(caller, id, ct);
        if (rule is null) return NotFound<RuleValidationDto>();
        var def = CurrentDefinition(rule, versions);
        if (def is null) return PromotionOutcome<RuleValidationDto>.Fail(400, "Rule has no valid definition.");
        return PromotionOutcome<RuleValidationDto>.Success(
            await ValidateDefinitionAsync(caller, scope, def, rule.Id, ct));
    }

    public async Task<PromotionOutcome<RuleDetailDto>> PublishAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default)
    {
        var (rule, versions, scope) = await LoadAsync(caller, id, ct);
        if (rule is null) return NotFound<RuleDetailDto>();
        if (rule.Status == PromotionRule.StatusArchived)
            return PromotionOutcome<RuleDetailDto>.Fail(400, "Archived rules cannot be published.");
        var def = CurrentDefinition(rule, versions);
        if (def is null || rule.CurrentVersionId is null)
            return PromotionOutcome<RuleDetailDto>.Fail(400, "Rule has no valid definition.");

        var validation = await ValidateDefinitionAsync(caller, scope, def, rule.Id, ct);
        if (validation.Errors.Count > 0) return PromotionOutcome<RuleDetailDto>.Invalid(validation);

        rule.PublishedVersionId = rule.CurrentVersionId;
        rule.Status = PromotionRule.StatusPublished;
        rule.UpdatedAt = DateTime.UtcNow;
        await _rules.SaveChangesAsync(ct);
        return PromotionOutcome<RuleDetailDto>.Success(await BuildDetailAsync(rule, versions, ct));
    }

    public Task<PromotionOutcome<RuleDetailDto>> PauseAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default) =>
        ChangeStatusAsync(caller, id, PromotionRule.StatusPublished, PromotionRule.StatusPaused,
            "Only published rules can be paused.", ct);

    public Task<PromotionOutcome<RuleDetailDto>> ResumeAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default) =>
        ChangeStatusAsync(caller, id, PromotionRule.StatusPaused, PromotionRule.StatusPublished,
            "Only paused rules can be resumed.", ct);

    public async Task<PromotionOutcome<RuleDetailDto>> ArchiveAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct = default)
    {
        var (rule, versions, _) = await LoadAsync(caller, id, ct);
        if (rule is null) return NotFound<RuleDetailDto>();
        if (rule.Status != PromotionRule.StatusArchived)
        {
            rule.Status = PromotionRule.StatusArchived;
            rule.UpdatedAt = DateTime.UtcNow;
            await _rules.SaveChangesAsync(ct);
        }
        return PromotionOutcome<RuleDetailDto>.Success(await BuildDetailAsync(rule, versions, ct));
    }

    private async Task<PromotionOutcome<RuleDetailDto>> ChangeStatusAsync(
        PromotionRuleCaller caller, Guid id, string from, string to, string wrongState, CancellationToken ct)
    {
        var (rule, versions, _) = await LoadAsync(caller, id, ct);
        if (rule is null) return NotFound<RuleDetailDto>();
        if (rule.Status != from) return PromotionOutcome<RuleDetailDto>.Fail(400, wrongState);
        rule.Status = to;
        rule.UpdatedAt = DateTime.UtcNow;
        await _rules.SaveChangesAsync(ct);
        return PromotionOutcome<RuleDetailDto>.Success(await BuildDetailAsync(rule, versions, ct));
    }

    // ── simulate ───────────────────────────────────────────────────────────

    public async Task<PromotionOutcome<SimulateResultDto>> SimulateAsync(
        PromotionRuleCaller caller, SimulateRequest request, CancellationToken ct = default)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            return PromotionOutcome<SimulateResultDto>.Fail(400, "At least one line is required.");
        if (request.Lines.Count > MaxSimulateLines)
            return PromotionOutcome<SimulateResultDto>.Fail(400, $"At most {MaxSimulateLines} lines.");
        if (request.Lines.Any(l => l.Quantity <= 0))
            return PromotionOutcome<SimulateResultDto>.Fail(400, "Quantity must be positive.");

        var scope = await ScopeAsync(caller, ct);
        PromotionRuleInput ruleInput;
        if (request.Definition is { ValueKind: JsonValueKind.Object } raw)
        {
            var def = PromotionDefinitionJson.TryParse(raw.GetRawText());
            if (def is null) return PromotionOutcome<SimulateResultDto>.Fail(400, "Invalid definition.");
            ruleInput = new PromotionRuleInput(request.RuleId ?? Guid.Empty, Guid.Empty, "preview", DateTimeOffset.UtcNow, def);
        }
        else if (request.RuleId is { } rid)
        {
            var (rule, versions, _) = await LoadAsync(caller, rid, ct);
            if (rule is null) return NotFound<SimulateResultDto>();
            var def = CurrentDefinition(rule, versions);
            if (def is null) return PromotionOutcome<SimulateResultDto>.Fail(400, "Rule has no valid definition.");
            ruleInput = new PromotionRuleInput(rule.Id, rule.CurrentVersionId ?? Guid.Empty, rule.Name,
                new DateTimeOffset(DateTime.SpecifyKind(rule.CreatedAt, DateTimeKind.Utc)), def);
        }
        else
        {
            return PromotionOutcome<SimulateResultDto>.Fail(400, "ruleId or definition is required.");
        }

        if (scope is not null && !scope.Contains(request.StoreId))
            return PromotionOutcome<SimulateResultDto>.Fail(403, "Store is outside your scope.");

        var itemsById = await LoadItemsAsync(caller, request.Lines.Select(l => l.ItemId), ct);
        var inputs = new List<PromotionLineInput>();
        foreach (var l in request.Lines)
        {
            if (!itemsById.TryGetValue(l.ItemId, out var item))
                return PromotionOutcome<SimulateResultDto>.Fail(400, $"Unknown item {l.ItemId}.");
            var price = l.UnitPrice ?? item.PriceRetail;
            if (price is null || price < 0)
                return PromotionOutcome<SimulateResultDto>.Fail(400, $"Item '{item.Name}' has no price.");
            inputs.Add(new PromotionLineInput(item.Id, item.CategoryId, Math.Round(price.Value, 2), l.Quantity));
        }

        var membership = request.Membership is null
            ? null
            : new PromotionMembership(Guid.Empty, request.Membership.TierId);
        var at = request.At ?? DateTimeOffset.UtcNow;
        var store = await _locations.GetByIdAsync(request.StoreId, ct);
        var storeTimeZone = store is not null && store.TenantId == caller.TenantId ? store.TimeZone : null;
        var result = PromotionEngine.Evaluate(inputs, [ruleInput], new PromotionContext(request.StoreId, at, 0, membership, storeTimeZone));

        var lines = new List<SimulateLineDto>();
        decimal total = 0m, regularTotal = 0m;
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var segs = result.Lines[i].Segments;
            var lineTotal = Math.Round(segs.Sum(s => s.Quantity * s.UnitPrice), 2);
            var regular = Math.Round(input.Quantity * input.UnitPrice, 2);
            total += lineTotal;
            regularTotal += regular;
            lines.Add(new SimulateLineDto(
                input.ItemId, itemsById[input.ItemId].Name, input.Quantity, input.UnitPrice,
                segs.Select(s => new SimulateSegmentDto(s.Quantity, s.UnitPrice, s.Label)).ToList(),
                lineTotal, regular));
        }
        return PromotionOutcome<SimulateResultDto>.Success(
            new SimulateResultDto(lines, total, regularTotal, regularTotal - total));
    }

    // ── lookups ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(
        PromotionRuleCaller caller, string? search, Guid? categoryId, int? limit, CancellationToken ct = default,
        IReadOnlyList<Guid>? ids = null)
    {
        if (ids is { Count: > 0 })
        {
            // Exact lookup (picker rehydration): search/categoryId/limit are ignored, max 50 ids.
            var wanted = ids.Distinct().Take(MaxLookupLimit).ToList();
            var found = await LoadItemsAsync(caller, wanted, ct);
            return wanted.Where(found.ContainsKey).Select(i => found[i])
                .Where(i => i.IsActive)
                .Select(i => new ItemLookupDto(
                    i.Id, i.Name, i.Barcodes.FirstOrDefault(), i.PriceRetail, i.CategoryId, i.Category?.Name))
                .ToList();
        }
        var take = Math.Clamp(limit ?? 20, 1, MaxLookupLimit);

        // Scanner suffixes / control chars / stray spaces must not break the search.
        var cleaned = new string((search ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        var compact = new string(cleaned.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var digitsOnly = compact.Length > 0 && compact.All(c => c is >= '0' and <= '9');
        var query = digitsOnly ? compact : cleaned;

        var (items, _) = await _items.GetPagedAsync(
            categoryId, null, null, string.IsNullOrWhiteSpace(query) ? null : query, null,
            "name", false, 1, take, null, null, null, ct);
        var merged = items.ToList();

        // Digits-only query (>= 3): also barcode substring + leading-zero-insensitive matches.
        if (digitsOnly && compact.Length >= 3)
        {
            var fragment = await _items.SearchByBarcodeFragmentAsync(caller.TenantId, compact, take, ct);
            var known = merged.Select(i => i.Id).ToHashSet();
            merged.AddRange(fragment.Where(i => (categoryId is null || i.CategoryId == categoryId) && known.Add(i.Id)));
        }

        var stripped = digitsOnly ? compact.TrimStart('0') : string.Empty;
        bool Exact(Item i) => digitsOnly && i.Barcodes.Any(b =>
            b == compact || (stripped.Length > 0 && b.TrimStart('0') == stripped));

        return merged
            .Where(i => i.TenantId == caller.TenantId && i.IsActive)
            .OrderByDescending(Exact).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .Select(i => new ItemLookupDto(
                i.Id, i.Name, i.Barcodes.FirstOrDefault(), i.PriceRetail, i.CategoryId, i.Category?.Name))
            .ToList();
    }

    public async Task<IReadOnlyList<TierLookupDto>> LookupTiersAsync(PromotionRuleCaller caller, CancellationToken ct = default) =>
        (await _loyalty.GetTierLadderAsync(caller.TenantId, ct))
            .Where(t => t.TenantId == caller.TenantId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new TierLookupDto(t.Id, t.Name, t.DiscountPercent, t.SortOrder))
            .ToList();

    // ── helpers ────────────────────────────────────────────────────────────

    private static PromotionOutcome<T> NotFound<T>() => PromotionOutcome<T>.Fail(404, "Rule not found.");

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>null = unrestricted (enterprise_admin / provider); otherwise the stores a manager may touch.</summary>
    private async Task<HashSet<Guid>?> ScopeAsync(PromotionRuleCaller caller, CancellationToken ct)
    {
        switch (caller.Role)
        {
            case AppRoles.EnterpriseAdmin:
            case AppRoles.Provider:
                return null;
            case AppRoles.StoreManager:
            case AppRoles.NetworkManager:
                var ids = (await _userLocations.GetLocationIdsForUserAsync(caller.TenantId, caller.UserId, ct)).ToHashSet();
                if (caller.StoreId is { } own) ids.Add(own);
                return ids;
            default:
                return [];
        }
    }

    private static bool InScope(HashSet<Guid>? scope, PromotionDefinition def) =>
        scope is null || (def.Scope.StoreIds.Count > 0 && def.Scope.StoreIds.All(scope.Contains));

    /// <summary>A rule is visible only when its current AND published definitions are inside the caller's scope.</summary>
    private static bool VisibleTo(HashSet<Guid>? scope, PromotionDefinition? current, PromotionDefinition? published)
    {
        if (scope is null) return true;
        if (current is null || !InScope(scope, current)) return false;
        return published is null || InScope(scope, published);
    }

    private async Task<(PromotionRule? Rule, IReadOnlyList<PromotionRuleVersion> Versions, HashSet<Guid>? Scope)> LoadAsync(
        PromotionRuleCaller caller, Guid id, CancellationToken ct)
    {
        var scope = await ScopeAsync(caller, ct);
        var rule = await _rules.GetByIdAsync(id, ct);
        if (rule is null || rule.TenantId != caller.TenantId) return (null, [], scope);
        var versions = await _rules.GetVersionsAsync(rule.Id, ct);
        var cur = PromotionDefinitionJson.TryParse(versions.FirstOrDefault(v => v.Id == rule.CurrentVersionId)?.DefinitionJson);
        var pub = PromotionDefinitionJson.TryParse(versions.FirstOrDefault(v => v.Id == rule.PublishedVersionId)?.DefinitionJson);
        return VisibleTo(scope, cur, pub) ? (rule, versions, scope) : (null, [], scope);
    }

    private static PromotionDefinition? CurrentDefinition(PromotionRule rule, IReadOnlyList<PromotionRuleVersion> versions) =>
        PromotionDefinitionJson.TryParse(versions.FirstOrDefault(v => v.Id == rule.CurrentVersionId)?.DefinitionJson);

    private sealed record Prepared(string? Name, string? Json, int Status, string? Error, RuleValidationDto? Validation)
    {
        public bool Failed => Error is not null || Validation is not null;
        public PromotionOutcome<T> Fail<T>() => new(default, Status, Error, Validation);
    }

    /// <summary>Name + definition checks shared by create and update (saving a draft needs no full validation).</summary>
    private async Task<Prepared> PrepareSaveAsync(
        PromotionRuleCaller caller, string? name, JsonElement definition, CancellationToken ct)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 200)
            return new Prepared(null, null, 400, "Name is required (max 200 characters).", null);
        if (definition.ValueKind != JsonValueKind.Object)
            return new Prepared(null, null, 400, "Definition is required.", null);
        var def = PromotionDefinitionJson.TryParse(definition.GetRawText());
        if (def is null) return new Prepared(null, null, 400, "Invalid definition.", null);

        var scope = await ScopeAsync(caller, ct);
        if (!InScope(scope, def))
        {
            var issue = new RuleIssueDto("store_scope_violation", "scope.storeIds",
                "Stores must be selected and inside your assigned stores.");
            return new Prepared(null, null, 400, null, new RuleValidationDto([issue], []));
        }
        return new Prepared(trimmed, PromotionDefinitionJson.Serialize(def), 200, null, null);
    }

    private async Task<RuleDetailDto> BuildDetailAsync(
        PromotionRule rule, IReadOnlyList<PromotionRuleVersion> versions, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        async Task<string?> NameOf(Guid? userId)
        {
            if (userId is not { } uid) return null;
            if (!names.TryGetValue(uid, out var n))
                names[uid] = n = (await _users.GetByIdAsync(uid, ct))?.FullName;
            return n;
        }

        async Task<RuleVersionDto?> Map(Guid? versionId)
        {
            var v = versions.FirstOrDefault(x => x.Id == versionId);
            if (v is null) return null;
            using var doc = JsonDocument.Parse(v.DefinitionJson);
            return new RuleVersionDto(v.Id, v.VersionNumber, doc.RootElement.Clone(), v.ChangeComment,
                await NameOf(v.CreatedBy), v.CreatedAt);
        }

        var summaries = new List<RuleVersionSummaryDto>();
        foreach (var v in versions.OrderByDescending(x => x.VersionNumber))
            summaries.Add(new RuleVersionSummaryDto(v.Id, v.VersionNumber, v.CreatedAt, await NameOf(v.CreatedBy),
                v.ChangeComment, v.Id == rule.PublishedVersionId));

        var current = await Map(rule.CurrentVersionId)
            ?? throw new InvalidOperationException($"Promotion rule {rule.Id} has no current version.");
        return new RuleDetailDto(rule.Id, rule.Name, rule.Description, rule.Status, current,
            await Map(rule.PublishedVersionId), summaries);
    }

    private async Task<Dictionary<Guid, Item>> LoadItemsAsync(
        PromotionRuleCaller caller, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Item>();
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var (items, _) = await _items.GetPagedAsync(
                null, null, null, null, chunk, null, null, 1, chunk.Length, null, null, null, ct);
            foreach (var i in items.Where(i => i.TenantId == caller.TenantId)) result[i.Id] = i;
        }
        return result;
    }
}
