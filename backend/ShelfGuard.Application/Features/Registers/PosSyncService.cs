using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Registers;

public sealed class PosSyncService : IPosSyncService
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 2000;

    private readonly ICatalogSyncRepository _catalog;
    private readonly IPromotionRuleRepository? _promotions;
    private readonly ICashRegisterRepository? _registers;
    private readonly ILocationRepository? _locations;

    public PosSyncService(
        ICatalogSyncRepository catalog,
        IPromotionRuleRepository? promotions = null,
        ICashRegisterRepository? registers = null,
        ILocationRepository? locations = null)
    {
        _locations = locations;
        _catalog = catalog;
        _promotions = promotions;
        _registers = registers;
    }

    public async Task<SyncCatalogResponse> GetCatalogAsync(
        DateTime? since, Guid? afterId, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var serverTime = DateTime.UtcNow;

        var rows = await _catalog.GetChangedSinceAsync(since, afterId, take, ct);
        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();

        var last = page.Count > 0 ? page[^1] : null;

        return new SyncCatalogResponse(
            Items: page.Select(r => new SyncCatalogItemDto(
                r.Id, r.Name, r.Barcodes, r.PriceRetail, r.VatRate, r.Unit, r.IsActive, r.UpdatedAt, r.CategoryId)).ToList(),
            HasMore: hasMore,
            NextSince: last?.UpdatedAt ?? since,
            NextAfterId: last?.Id ?? afterId,
            ServerTime: serverTime);
    }

    public async Task<(SyncPromotionsResponse? Result, string? Error, int? StatusCode)> GetPromotionsAsync(
        Guid tenantId, Guid? registerId, Guid? fallbackStoreId, string? etag, CancellationToken ct = default)
    {
        Guid storeId;
        if (registerId is Guid regId)
        {
            // Operator token: the register's store is authoritative (never a client-supplied id).
            var register = _registers is null ? null : await _registers.GetByIdAsync(regId, ct);
            if (register is null || register.TenantId != tenantId)
                return (null, "Register not found.", 403);
            storeId = register.StoreId;
        }
        else if (fallbackStoreId is Guid sid && sid != Guid.Empty)
        {
            storeId = sid;
        }
        else
        {
            return (null, "storeId is required.", 400);
        }

        var storeTimeZone = _locations is null ? null : (await _locations.GetByIdAsync(storeId, ct))?.TimeZone;
        var serverTime = DateTime.UtcNow;
        var now = new DateTimeOffset(serverTime, TimeSpan.Zero);
        var rows = _promotions is null ? [] : await _promotions.GetPublishedAsync(tenantId, ct);

        // Published, structurally valid, covering this store, and not already over (valid now or
        // in the future). Time-of-day / weekday / audience are evaluated by the cashier's engine.
        var applicable = rows
            .Select(r => (Row: r, Def: PromotionDefinitionJson.TryParse(r.DefinitionJson)))
            .Where(x => PromotionEngine.IsValid(x.Def)
                        && (x.Def!.Scope.StoreIds.Count == 0 || x.Def.Scope.StoreIds.Contains(storeId))
                        && (x.Def.Scope.ValidTo is null || x.Def.Scope.ValidTo > now))
            .OrderBy(x => x.Row.RuleId)
            .ToList();

        // The store time zone is part of the etag: changing it changes how scope is evaluated.
        var current = ComputeEtag(applicable.Select(x =>
                $"{x.Row.RuleId:D}|{x.Row.VersionId:D}|{x.Row.Name}|{x.Def!.Scope.ValidFrom:O}|{x.Def.Scope.ValidTo:O}")
            .Append($"tz|{storeTimeZone}"));

        if (string.Equals(etag, current, StringComparison.Ordinal))
            return (new SyncPromotionsResponse(current, true, serverTime, [], storeTimeZone), null, null);

        var rules = applicable.Select(x =>
        {
            using var doc = JsonDocument.Parse(x.Row.DefinitionJson);
            return new SyncPromotionRuleDto(
                x.Row.RuleId, x.Row.VersionId, x.Row.Name,
                DateTime.SpecifyKind(x.Row.CreatedAt, DateTimeKind.Utc), doc.RootElement.Clone());
        }).ToList();

        return (new SyncPromotionsResponse(current, false, serverTime, rules, storeTimeZone), null, null);
    }

    /// <summary>Hash of the applicable set (rule ids, immutable version ids, names, valid windows).</summary>
    public static string ComputeEtag(IEnumerable<string> parts) =>
        "p1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))))
            .ToLowerInvariant()[..32];
}
