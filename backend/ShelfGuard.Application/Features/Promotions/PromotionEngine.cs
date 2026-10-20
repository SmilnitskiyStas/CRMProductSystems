namespace ShelfGuard.Application.Features.Promotions;

/// <summary>A published rule version fed to the engine.</summary>
/// <param name="CreatedAt">Rule creation time — "older rule" tie-break.</param>
public sealed record PromotionRuleInput(
    Guid Id, Guid VersionId, string Name, DateTimeOffset CreatedAt, PromotionDefinition Definition);

/// <param name="UnitPrice">Price per unit before promotions (retail minus any stock/expiry discount).</param>
/// <param name="HasStockDiscount">The unit already carries a stock/expiry Discount.</param>
/// <param name="RegularPrice">Shelf price before the stock discount; base of the loyalty tier
/// discount (defaults to UnitPrice). Keeps the pre-existing tier maths exact.</param>
public sealed record PromotionLineInput(
    Guid ItemId, Guid? CategoryId, decimal UnitPrice, decimal Quantity,
    bool HasStockDiscount = false, decimal? RegularPrice = null);

/// <param name="TierPercent">Loyalty tier discount percent competing with promos (0 = none).</param>
public sealed record PromotionMembership(Guid Id, Guid? TierId, decimal TierPercent = 0m);

/// <param name="At">Pricing instant (UTC).</param>
/// <param name="UtcOffsetMinutes">Fixed store-local offset, used only when <paramref name="StoreTimeZone"/> is null/unknown.</param>
/// <param name="StoreTimeZone">IANA id of the store (e.g. "Europe/Kyiv"); weekday / time-of-day scope is evaluated in
/// its local time incl. DST. Null/unknown = UTC (+ UtcOffsetMinutes) as before.</param>
public sealed record PromotionContext(
    Guid StoreId, DateTimeOffset At, int UtcOffsetMinutes = 0, PromotionMembership? Membership = null,
    string? StoreTimeZone = null);

/// <param name="RuleId">Rule that priced these units; null = regular price or loyalty tier price.</param>
public sealed record PromotionSegment(decimal Quantity, decimal UnitPrice, Guid? RuleId = null, string? Label = null);

public sealed record PromotionLineResult(IReadOnlyList<PromotionSegment> Segments);

public sealed record PromotionResult(IReadOnlyList<PromotionLineResult> Lines);

/// <summary>
/// Deterministic, dependency-free promotion engine. The SAME algorithm is mirrored in the cashier
/// (TypeScript) and both are pinned by backend/ShelfGuard.Tests/Promotions/golden-vectors.json.
///
/// Algorithm (per receipt):
///  0. Active rules = valid definition ∧ in scope (store, validFrom ≤ at &lt; validTo, store-local
///     weekday and [timeFrom, timeTo) window) ∧ audience. Rule order = priority ASC, createdAt ASC,
///     id (lowercase "D" string) ordinal ASC.
///  1. Each line is expanded into units: floor(quantity) whole units plus one fractional piece
///     (weight = remainder). Fractional pieces never join buy_n_get_m groups.
///  2. "Best simple price" of a unit = min of: unit price, loyalty tier price, every eligible
///     percent_off / fixed_off / fixed_price price (ignoring per-receipt limits).
///  3. buy_n_get_m (repeat until nothing improves): for every group rule with capacity left, take
///     its eligible unclaimed whole units (per_item: partitioned by item in first-appearance order;
///     across_set: all together), sort by unit price DESC, line ASC, unit ASC, chunk into groups of
///     buyQty+getQty (incomplete tail dropped); in each group the first buyQty units pay their unit
///     price and the last getQty (= cheapest) units pay the get price. A group is kept only when its
///     total is strictly below the sum of its units' best simple prices (gain &gt; 0), at most the
///     remaining maxApplicationsPerReceipt groups, in chunk order. The rule with the largest total
///     gain is applied (ties: rule order); its units are claimed.
///  4. Every unclaimed unit/piece, in order of (best saving × weight) DESC, line ASC, unit ASC,
///     takes its cheapest candidate that still has capacity: price ASC, tier before rules on equal
///     price, then rule order. One application of a simple rule = one unit/piece.
///  5. Units of a line are merged into segments by (unitPrice, ruleId) in first-appearance order.
/// Prices: promo price = clamp(round(p, 2, half away from zero), 0.01, unit price) and is only a
/// candidate when strictly below the unit price. Tier price = max(0, unitPrice −
/// round(regularPrice × tier% / 100, 2, half to even)) — the pre-existing tier formula.
/// </summary>
public static class PromotionEngine
{
    public const decimal MinPromoPrice = 0.01m;

    /// <summary>Safety valve: above this many whole units per receipt only the tier is evaluated.</summary>
    public const int MaxUnitsPerReceipt = 20_000;

    private const int TierRank = -1;

    public static PromotionResult Evaluate(
        IReadOnlyList<PromotionLineInput> lines,
        IReadOnlyList<PromotionRuleInput> rules,
        PromotionContext context)
    {
        var active = rules
            .Where(r => IsValid(r.Definition) && IsInScope(r.Definition, context) && IsAudienceMatch(r.Definition, context))
            .OrderBy(r => r.Definition.Priority)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

        var totalWhole = lines.Sum(l => l.Quantity > 0 ? Math.Floor(l.Quantity) : 0m);
        if (totalWhole > MaxUnitsPerReceipt) active = [];

        var tierPercent = context.Membership?.TierPercent ?? 0m;

        // 1. units
        var units = new List<Unit>();
        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            if (line.Quantity <= 0) continue;
            if (active.Count == 0)
            {
                // No promotion can apply: one unit per line is enough (identical prices).
                units.Add(new Unit(li, 0, line.Quantity, false, line));
                continue;
            }
            // totalWhole <= MaxUnitsPerReceipt here (otherwise active was emptied above).
            var whole = (int)Math.Floor(line.Quantity);
            for (var k = 0; k < whole; k++) units.Add(new Unit(li, k, 1m, true, line));
            var frac = line.Quantity - whole;
            if (frac > 0) units.Add(new Unit(li, whole, frac, false, line));
        }

        var used = new int[active.Count];
        int Capacity(int rank) => active[rank].Definition.Limits.MaxApplicationsPerReceipt is int max
            ? Math.Max(0, max) - used[rank]
            : int.MaxValue;

        // 2. per-unit simple candidates (sorted) + best simple price
        foreach (var u in units)
        {
            var cands = new List<Candidate>();
            if (tierPercent > 0)
            {
                var regular = u.Line.RegularPrice ?? u.Line.UnitPrice;
                var tierPrice = Math.Max(0m, u.Base - Math.Round(regular * tierPercent / 100m, 2, MidpointRounding.ToEven));
                if (tierPrice < u.Base) cands.Add(new Candidate(tierPrice, TierRank));
            }
            for (var rank = 0; rank < active.Count; rank++)
            {
                var def = active[rank].Definition;
                if (def.Type == PromotionRuleTypes.BuyNGetM || !IsEligible(def, u)) continue;
                var p = SimplePrice(def, u.Base);
                if (p < u.Base) cands.Add(new Candidate(p, rank));
            }
            u.Candidates = cands
                .OrderBy(c => c.Price)
                .ThenBy(c => c.Rank) // TierRank (-1) sorts before every rule
                .ToList();
            u.BestSimple = u.Candidates.Count > 0 ? u.Candidates[0].Price : u.Base;
        }

        // 3. buy_n_get_m groups
        while (true)
        {
            int bestRank = -1;
            decimal bestGain = 0m;
            List<List<Unit>>? bestGroups = null;

            for (var rank = 0; rank < active.Count; rank++)
            {
                var def = active[rank].Definition;
                if (def.Type != PromotionRuleTypes.BuyNGetM) continue;
                var cap = Capacity(rank);
                if (cap <= 0) continue;

                var (groups, gain) = FormGroups(def, units, cap);
                if (groups.Count > 0 && gain > bestGain)
                {
                    bestRank = rank;
                    bestGain = gain;
                    bestGroups = groups;
                }
            }

            if (bestRank < 0 || bestGroups is null) break;

            var rule = active[bestRank];
            var buy = rule.Definition.Params.BuyQty!.Value;
            foreach (var group in bestGroups)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    var u = group[i];
                    u.Claimed = true;
                    if (i < buy)
                    {
                        u.Price = u.Base;
                    }
                    else
                    {
                        u.Price = GetUnitPrice(rule.Definition, u.Base);
                        u.RuleId = rule.Id;
                        u.Label = rule.Name;
                    }
                }
            }
            used[bestRank] += bestGroups.Count;
        }

        // 4. simple assignment for the rest
        var rest = units.Where(u => !u.Claimed)
            .OrderByDescending(u => (u.Base - u.BestSimple) * u.Weight)
            .ThenBy(u => u.LineIndex)
            .ThenBy(u => u.UnitIndex)
            .ToList();
        foreach (var u in rest)
        {
            u.Price = u.Base;
            foreach (var c in u.Candidates)
            {
                if (c.Rank == TierRank)
                {
                    u.Price = c.Price;
                    break;
                }
                if (Capacity(c.Rank) <= 0) continue;
                used[c.Rank]++;
                u.Price = c.Price;
                u.RuleId = active[c.Rank].Id;
                u.Label = active[c.Rank].Name;
                break;
            }
            u.Claimed = true;
        }

        // 5. segments
        var result = new List<PromotionLineResult>(lines.Count);
        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            if (line.Quantity <= 0)
            {
                result.Add(new PromotionLineResult([new PromotionSegment(line.Quantity, line.UnitPrice)]));
                continue;
            }

            var segs = new List<(decimal Price, Guid? RuleId, string? Label, decimal Qty)>();
            foreach (var u in units.Where(x => x.LineIndex == li).OrderBy(x => x.UnitIndex))
            {
                var idx = segs.FindIndex(s => s.Price == u.Price && s.RuleId == u.RuleId);
                if (idx < 0) segs.Add((u.Price, u.RuleId, u.Label, u.Weight));
                else segs[idx] = segs[idx] with { Qty = segs[idx].Qty + u.Weight };
            }
            result.Add(new PromotionLineResult(
                segs.Select(s => new PromotionSegment(s.Qty, s.Price, s.RuleId, s.Label)).ToList()));
        }

        return new PromotionResult(result);
    }

    // ── groups ─────────────────────────────────────────────────────────────

    private static (List<List<Unit>> Groups, decimal Gain) FormGroups(
        PromotionDefinition def, List<Unit> units, int capacity)
    {
        var buy = def.Params.BuyQty!.Value;
        var get = def.Params.GetQty!.Value;
        var size = buy + get;

        var eligible = units.Where(u => !u.Claimed && u.Whole && IsEligible(def, u)).ToList();
        IEnumerable<List<Unit>> partitions = (def.Params.MatchMode ?? PromotionMatchModes.PerItem) == PromotionMatchModes.AcrossSet
            ? [eligible]
            : eligible.GroupBy(u => u.Line.ItemId)
                .OrderBy(g => g.Min(u => u.LineIndex))
                .Select(g => g.ToList());

        var groups = new List<List<Unit>>();
        var gain = 0m;
        foreach (var part in partitions)
        {
            var sorted = part
                .OrderByDescending(u => u.Base)
                .ThenBy(u => u.LineIndex)
                .ThenBy(u => u.UnitIndex)
                .ToList();
            for (var start = 0; start + size <= sorted.Count; start += size)
            {
                if (groups.Count >= capacity) return (groups, gain);
                var chunk = sorted.GetRange(start, size);
                var cost = 0m;
                var alt = 0m;
                for (var i = 0; i < size; i++)
                {
                    cost += i < buy ? chunk[i].Base : GetUnitPrice(def, chunk[i].Base);
                    alt += chunk[i].BestSimple;
                }
                if (alt - cost > 0m)
                {
                    groups.Add(chunk);
                    gain += alt - cost;
                }
            }
        }
        return (groups, gain);
    }

    // ── prices ─────────────────────────────────────────────────────────────

    private static decimal ClampPromo(decimal price, decimal unitPrice) =>
        Math.Min(unitPrice, Math.Max(MinPromoPrice, Math.Round(price, 2, MidpointRounding.AwayFromZero)));

    private static decimal SimplePrice(PromotionDefinition def, decimal unitPrice) => def.Type switch
    {
        PromotionRuleTypes.PercentOff => ClampPromo(unitPrice * (100m - def.Params.Percent!.Value) / 100m, unitPrice),
        PromotionRuleTypes.FixedOff => ClampPromo(unitPrice - def.Params.Amount!.Value, unitPrice),
        PromotionRuleTypes.FixedPrice => ClampPromo(def.Params.Price!.Value, unitPrice),
        _ => unitPrice,
    };

    private static decimal GetUnitPrice(PromotionDefinition def, decimal unitPrice) =>
        def.Params.GetPrice is decimal gp
            ? ClampPromo(gp, unitPrice)
            : ClampPromo(unitPrice * (100m - def.Params.GetPercent!.Value) / 100m, unitPrice);

    // ── eligibility ────────────────────────────────────────────────────────

    private static bool IsEligible(PromotionDefinition def, Unit u) =>
        !(def.ExcludeAlreadyDiscounted && u.Line.HasStockDiscount)
        && (def.Targets.ItemIds.Contains(u.Line.ItemId)
            || (u.Line.CategoryId is Guid c && def.Targets.CategoryIds.Contains(c)));

    /// <summary>Structural validity; an invalid definition is ignored (never discounts).</summary>
    public static bool IsValid(PromotionDefinition? def)
    {
        if (def is null) return false;
        if (def.Targets is null || def.Scope is null || def.Audience is null || def.Params is null || def.Limits is null)
            return false;
        if (def.Targets.ItemIds.Count == 0 && def.Targets.CategoryIds.Count == 0) return false;
        if (def.Audience.Kind is not (PromotionAudienceKinds.All or PromotionAudienceKinds.LoyaltyMember
                or PromotionAudienceKinds.LoyaltyTier))
            return false;
        if (!string.IsNullOrWhiteSpace(def.Scope.TimeFrom) && PromotionDefinitionJson.ParseTime(def.Scope.TimeFrom) is null)
            return false;
        if (!string.IsNullOrWhiteSpace(def.Scope.TimeTo) && PromotionDefinitionJson.ParseTime(def.Scope.TimeTo) is null)
            return false;
        if (def.Scope.Weekdays.Any(d => d is < 1 or > 7)) return false;

        var p = def.Params;
        return def.Type switch
        {
            PromotionRuleTypes.PercentOff => p.Percent is > 0m and <= 100m,
            PromotionRuleTypes.FixedOff => p.Amount is > 0m,
            PromotionRuleTypes.FixedPrice => p.Price is >= MinPromoPrice,
            PromotionRuleTypes.BuyNGetM =>
                p.BuyQty is >= 1 && p.GetQty is >= 1
                && (p.GetPrice is >= 0m || (p.GetPrice is null && p.GetPercent is > 0m and <= 100m))
                && (p.MatchMode is null or PromotionMatchModes.PerItem or PromotionMatchModes.AcrossSet),
            _ => false,
        };
    }

    public static bool IsInScope(PromotionDefinition def, PromotionContext ctx)
    {
        var s = def.Scope;
        if (s.StoreIds.Count > 0 && !s.StoreIds.Contains(ctx.StoreId)) return false;
        if (s.ValidFrom is DateTimeOffset from && ctx.At < from) return false;
        if (s.ValidTo is DateTimeOffset to && ctx.At >= to) return false;

        // Weekdays and the time window are evaluated in store-local time: the IANA zone (DST
        // aware) when known, otherwise the fixed UtcOffsetMinutes (UTC when 0).
        var local = StoreTimeZones.Resolve(ctx.StoreTimeZone) is { } tz
            ? TimeZoneInfo.ConvertTime(ctx.At, tz).DateTime
            : ctx.At.UtcDateTime.AddMinutes(ctx.UtcOffsetMinutes);

        var tFrom = PromotionDefinitionJson.ParseTime(s.TimeFrom);
        var tTo = PromotionDefinitionJson.ParseTime(s.TimeTo);
        var t = local.TimeOfDay;

        // A window crossing midnight (timeFrom > timeTo) belongs to the day it STARTS on:
        // Mon 22:00-02:00 covers Mon 22:00 -> Tue 02:00, so in its after-midnight part the
        // weekday that counts is the previous local day.
        var weekdayDate = local.Date;
        if (tFrom is TimeSpan wf && tTo is TimeSpan we && wf > we && t < we) weekdayDate = weekdayDate.AddDays(-1);
        if (s.Weekdays.Count > 0)
        {
            var iso = weekdayDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)weekdayDate.DayOfWeek;
            if (!s.Weekdays.Contains(iso)) return false;
        }

        if (tFrom is TimeSpan f && tTo is TimeSpan e)
            return f <= e ? t >= f && t < e : t >= f || t < e;
        if (tFrom is TimeSpan f2) return t >= f2;
        if (tTo is TimeSpan e2) return t < e2;
        return true;
    }

    public static bool IsAudienceMatch(PromotionDefinition def, PromotionContext ctx) => def.Audience.Kind switch
    {
        PromotionAudienceKinds.All => true,
        PromotionAudienceKinds.LoyaltyMember => ctx.Membership is not null,
        PromotionAudienceKinds.LoyaltyTier => ctx.Membership?.TierId is Guid tier && def.Audience.TierIds.Contains(tier),
        _ => false,
    };

    // ── private types ──────────────────────────────────────────────────────

    private sealed record Candidate(decimal Price, int Rank);

    private sealed class Unit(int lineIndex, int unitIndex, decimal weight, bool whole, PromotionLineInput line)
    {
        public int LineIndex { get; } = lineIndex;
        public int UnitIndex { get; } = unitIndex;
        public decimal Weight { get; } = weight;
        public bool Whole { get; } = whole;
        public PromotionLineInput Line { get; } = line;
        public decimal Base => Line.UnitPrice;
        public List<Candidate> Candidates { get; set; } = [];
        public decimal BestSimple { get; set; }
        public bool Claimed { get; set; }
        public decimal Price { get; set; }
        public Guid? RuleId { get; set; }
        public string? Label { get; set; }
    }
}
