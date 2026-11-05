using System.Text.Json.Nodes;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Published navigation explicitly exposes consumer marketing sections. Keep their API
/// gates and block visibility consistent with the mobile route policy, even for older
/// documents whose seed features still say false. This never enables licensed loyalty.
/// Only the served document is reconciled; saved draft/published versions are unchanged.
/// </summary>
public static class ConsumerNavigationAvailability
{
    public static void Apply(JsonObject document)
    {
        if (document["navigation"] is not JsonArray navigation) return;
        var features = document["features"] as JsonObject ?? new JsonObject();
        foreach (var item in navigation)
        {
            if (item is not JsonObject entry || entry["type"] is not JsonValue value ||
                !value.TryGetValue<string>(out var type)) continue;
            if (type is "promotions" or "catalog" or "coupons" or "news")
                features[type] = true;
        }
        document["features"] = features;
    }
}
