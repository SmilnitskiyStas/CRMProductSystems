using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Application.Features.Pricing;

public sealed record BasePriceCandidate(PriceList List, PriceListItem Line);
public sealed record PlannedBaseSelection(BasePriceCandidate? Selected, DateTime? NextBoundary);

/// <summary>The same current-revision precedence for Desktop offers and online POS.</summary>
public static class PlannedBasePricing
{
    public static bool Matches(PosBasePriceDecision decision, BasketPricingLine input, DateTime at, bool frozen = false)
    {
        if (decision.ItemId != input.ItemId || decision.Policy != PricingRules.Policy
            || (!frozen && decision.EvaluatedAt != at) || (frozen && decision.EvaluatedAt > at.AddMinutes(5))
            || decision.Price != input.RetailPrice || decision.VatRate != input.VatRate
            || decision.NextBoundary is DateTime boundary && boundary <= at) return false;
        return decision.Source switch
        {
            "legacy_item_base" => decision.PriceListId == null && decision.ListKey == null
                && decision.Version == null && decision.Fingerprint == null,
            "planned_price_list" => decision.PriceListId is Guid list && list != Guid.Empty
                && decision.ListKey is Guid key && key != Guid.Empty && decision.Version is > 0
                && decision.Fingerprint is { Length: 64 } hash && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            _ => false
        };
    }
    public static PlannedBaseSelection Select(Guid tenant, Guid location, Guid item, DateTime at,
        IReadOnlyList<BasePriceCandidate> candidates)
    {
        var scoped = candidates.Where(c => c.List.TenantId == tenant && c.Line.TenantId == tenant
            && c.Line.PriceListId == c.List.Id && c.Line.ItemId == item && c.List.IsActive
            && (c.List.LocationId == null || c.List.LocationId == location)).ToArray();
        if (scoped.Length > 1000) throw new PosPricingAuthorityException("pricing_limit");
        var selected = scoped.Where(c => c.List.ValidFrom <= at && (c.List.ValidUntil == null || at < c.List.ValidUntil))
            .OrderBy(c => c.List.LocationId == null ? 1 : 0).ThenBy(c => c.List.Priority)
            .ThenBy(c => c.List.ListKey).FirstOrDefault();
        var next = scoped.SelectMany(c => new DateTime?[] { c.List.ValidFrom, c.List.ValidUntil })
            .Where(t => t > at).OrderBy(t => t).FirstOrDefault();
        return new(selected, next);
    }
}

public interface IPosOnlinePricingGate { bool Allows(Guid tenant, Guid location); IEnumerable<Guid> Locations(Guid tenant) => []; }
public interface IPosOfflinePricingGate { bool Allows(Guid tenant, Guid location); IEnumerable<Guid> Locations(Guid tenant) => []; }
public sealed class PosOfflinePricingGate(bool enabled, IEnumerable<InventoryPostingScope> scopes) : IPosOfflinePricingGate
{
    private readonly PosOnlinePricingGate gate = new(enabled, scopes);
    public bool Allows(Guid tenant, Guid location) => gate.Allows(tenant, location);
    public IEnumerable<Guid> Locations(Guid tenant) => gate.Locations(tenant);
}

public sealed class PosOnlinePricingGate : IPosOnlinePricingGate
{
    private readonly bool enabled;
    private readonly HashSet<InventoryPostingScope> scopes;
    public PosOnlinePricingGate(bool enabled, IEnumerable<InventoryPostingScope> scopes)
    {
        this.enabled = enabled; this.scopes = scopes.ToHashSet();
        if (this.scopes.Count > 100 || this.scopes.Any(s => s.TenantId == Guid.Empty || s.LocationId == Guid.Empty)
            || enabled && this.scopes.Count == 0)
            throw new ArgumentException("Online POS pricing requires explicit tenant/location scopes (1–100 when enabled).");
    }
    public bool Allows(Guid tenant, Guid location) => enabled && scopes.Contains(new(tenant, location));
    public IEnumerable<Guid> Locations(Guid tenant) => enabled ? scopes.Where(s => s.TenantId == tenant).Select(s => s.LocationId) : [];
}

public sealed record PosBasePriceItem(Guid ItemId, string Unit, decimal? LegacyPrice, decimal LegacyVat);
public sealed record PosBasePriceDecision(Guid ItemId, string Policy, string Source, decimal Price, decimal VatRate,
    Guid? PriceListId, Guid? ListKey, int? Version, string? Fingerprint, DateTime EvaluatedAt, DateTime? NextBoundary,
    decimal? LegacyPrice = null, decimal? LegacyVatRate = null);

public interface IPosBasePriceResolver
{
    bool IsEnabled(Guid tenant, Guid location);
    Task<IReadOnlyDictionary<Guid, PosBasePriceDecision>> ResolveAsync(Guid tenant, Guid location, DateTime at,
        IReadOnlyList<PosBasePriceItem> items, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, PosBasePriceDecision>> ResolveAvailableAsync(Guid tenant, Guid location, DateTime at,
        IReadOnlyList<PosBasePriceItem> items, CancellationToken ct = default) => ResolveAsync(tenant, location, at, items, ct);
}

public sealed class PosPricingAuthorityException(string code) : Exception(code) { public string Code { get; } = code; }
